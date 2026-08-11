using MassTransit;
using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.UnitTests.Consumers;

/// <summary>
/// GH-604 — IotDeviceWentOfflineConsumer: resolve Manager+Admin → Push + InApp.
/// Recipient rỗng → skip (không gửi).
/// </summary>
public class IotDeviceWentOfflineConsumerTests
{
    private static IotDeviceWentOfflineEvent MakeEvent() => new(
        IotDeviceId: Guid.NewGuid(),
        DeviceCode: "DEV-01",
        DisplayName: "Gateway A",
        SiteId: Guid.NewGuid(),
        SiteName: "Site Hanoi",
        LastSeenAt: new DateTime(2026, 6, 22, 10, 0, 0, DateTimeKind.Utc),
        DetectedAt: new DateTime(2026, 6, 22, 10, 6, 0, DateTimeKind.Utc),
        OfflineDurationSeconds: 360,
        AffectedBatteryCount: 3,
        AlertId: Guid.NewGuid());

    [Fact]
    public async Task Consume_ShouldDispatch_PushAndInApp()
    {
        var (harness, written, _) =
            await ConsumerTestHarness.StartAsync<IotDeviceWentOfflineConsumer>();

        var evt = MakeEvent();
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<IotDeviceWentOfflineEvent>()).Should().BeTrue();

        written.Should().HaveCount(2);
        written.Should().Contain(c => c.Channel == NotificationChannelEnum.Push);
        written.Should().Contain(c => c.Channel == NotificationChannelEnum.InApp);
        written.Should().AllSatisfy(c =>
        {
            c.Type.Should().Be(NotificationTypeEnum.IotDeviceWentOffline);
            c.EntityType.Should().Be("IotDevice");
            c.EntityId.Should().Be(evt.IotDeviceId);
        });

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_NoRecipientResolved_ShouldSkip()
    {
        var (harness, written, _) =
            await ConsumerTestHarness.StartAsync<IotDeviceWentOfflineConsumer>(Array.Empty<Guid>());

        await harness.Bus.Publish(MakeEvent());
        (await harness.Consumed.Any<IotDeviceWentOfflineEvent>()).Should().BeTrue();

        written.Should().BeEmpty();
        await harness.Stop();
    }

    [Fact]
    public async Task Consume_IncludesSiteCustomer_AndDeduplicatesRecipients()
    {
        var operationsUser = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var (harness, written, _) =
            await ConsumerTestHarness.StartAsync<IotDeviceWentOfflineConsumer>(
                new[] { operationsUser, customer });

        await harness.Bus.Publish(MakeEvent() with { CustomerId = customer });
        (await harness.Consumed.Any<IotDeviceWentOfflineEvent>()).Should().BeTrue();

        written.Should().HaveCount(4, "two distinct recipients each receive InApp and Push");
        written.Select(c => c.UserId).Distinct().Should().BeEquivalentTo(new[] { operationsUser, customer });
        await harness.Stop();
    }

    [Fact]
    public async Task Consume_DifferentMessagesForSameIncident_WritesOnlyOnce()
    {
        var keys = new HashSet<string>();
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.TrySetIfNotExistsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, string _, TimeSpan _, CancellationToken _) => keys.Add(key));
        cache.Setup(c => c.TryRefreshLeaseAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var (harness, written, _) =
            await ConsumerTestHarness.StartAsync<IotDeviceWentOfflineConsumer>(cache: cache.Object);
        var evt = MakeEvent();

        await harness.Bus.Publish(evt, x => x.MessageId = Guid.NewGuid());
        await harness.Bus.Publish(evt, x => x.MessageId = Guid.NewGuid());

        (await harness.Consumed.SelectAsync<IotDeviceWentOfflineEvent>().Count()).Should().Be(2);
        written.Should().HaveCount(2, "business dedupe uses AlertId, not only broker MessageId");
        await harness.Stop();
    }
}
