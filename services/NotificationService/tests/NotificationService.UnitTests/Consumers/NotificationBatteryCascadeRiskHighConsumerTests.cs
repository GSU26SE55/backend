using FluentAssertions;
using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

/// <summary>
/// Sprint Bonus NS-14 (#658, R3) — NotificationService consume BatteryCascadeRiskHighEvent →
/// notify Manager/Admin (in-app + push + email).
/// </summary>
public class BatteryCascadeRiskHighConsumerTests
{
    private static BatteryCascadeRiskHighEvent Event() => new(
        BatteryAssetId: Guid.NewGuid(),
        SiteId: Guid.NewGuid(),
        CustomerId: Guid.NewGuid(),
        AssetSerialNumber: "BAT-001",
        CascadeRiskScore: 0.82m,
        RelatedTicketId: Guid.NewGuid(),
        DetectedAt: new DateTime(2026, 7, 8, 9, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task CascadeRiskHigh_Writes_InAppPushEmail_ToManagerAdmin()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<NotificationBatteryCascadeRiskHighConsumer>();
        var evt = Event();

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<BatteryCascadeRiskHighEvent>()).Should().BeTrue();

        // 3 channel (InApp + Push + Email) × 1 recipient mặc định = 3 record.
        written.Should().HaveCount(3);
        written.Select(n => n.Channel).Should().BeEquivalentTo(new[]
        {
            NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email
        });
        written.Should().AllSatisfy(n =>
        {
            n.Type.Should().Be(NotificationTypeEnum.CascadeRiskHigh);
            n.UserId.Should().Be(ConsumerTestHarness.DefaultRecipient);
            n.EntityType.Should().Be("BatteryAsset");
            n.EntityId.Should().Be(evt.BatteryAssetId);
            n.Body.Should().Contain("BAT-001");
        });

        await harness.Stop();
    }

    [Fact]
    public async Task CascadeRiskHigh_NoRecipient_WritesNothing()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<NotificationBatteryCascadeRiskHighConsumer>(
            recipients: Array.Empty<Guid>());

        await harness.Bus.Publish(Event());
        (await harness.Consumed.Any<BatteryCascadeRiskHighEvent>()).Should().BeTrue();

        written.Should().BeEmpty("không có Manager/Admin → skip, không ghi notification rỗng");

        await harness.Stop();
    }

    [Fact]
    public async Task CascadeRiskHigh_DuplicateMessage_Debounced()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<NotificationBatteryCascadeRiskHighConsumer>(
            cache: ConsumerTestHarness.AlreadySeenCache());

        await harness.Bus.Publish(Event());
        (await harness.Consumed.Any<BatteryCascadeRiskHighEvent>()).Should().BeTrue();

        written.Should().BeEmpty("message đã xử lý (debounce) → skip");

        await harness.Stop();
    }
}
