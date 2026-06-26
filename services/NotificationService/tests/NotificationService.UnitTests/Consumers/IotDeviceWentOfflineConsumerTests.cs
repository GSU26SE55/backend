using MassTransit;
using MassTransit.Testing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.Consumers;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Services;
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
    private static async Task<ITestHarness> StartHarness(IMediator mediator, IReadOnlyList<Guid>? recipients = null, ICacheService? cache = null)
    {
        var resolver = new Mock<IRecipientResolver>();
        resolver.Setup(x => x.GetActiveByRoleAsync(It.IsAny<CancellationToken>(), It.IsAny<string[]>()))
            .ReturnsAsync(recipients ?? new[] { Guid.NewGuid() });

        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x => x.AddConsumer<IotDeviceWentOfflineConsumer>())
            .AddSingleton(mediator)
            .AddSingleton(resolver.Object)
            .AddSingleton(cache ?? ConsumerTestHarness.ProceedCache())
            .AddSingleton(NullLogger<IotDeviceWentOfflineConsumer>.Instance)
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return harness;
    }

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

    private static Mock<IMediator> CaptureMediator(List<CreateNotificationCommand> sink)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) => sink.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });
        return mediator;
    }

    [Fact]
    public async Task Consume_ShouldDispatch_PushAndInApp()
    {
        var calls = new List<CreateNotificationCommand>();
        var harness = await StartHarness(CaptureMediator(calls).Object);

        var evt = MakeEvent();
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<IotDeviceWentOfflineEvent>()).Should().BeTrue();

        calls.Should().HaveCount(2);
        calls.Should().Contain(c => c.Channel == NotificationChannelEnum.Push);
        calls.Should().Contain(c => c.Channel == NotificationChannelEnum.InApp);
        calls.Should().AllSatisfy(c =>
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
        var calls = new List<CreateNotificationCommand>();
        var harness = await StartHarness(CaptureMediator(calls).Object, Array.Empty<Guid>());

        await harness.Bus.Publish(MakeEvent());
        (await harness.Consumed.Any<IotDeviceWentOfflineEvent>()).Should().BeTrue();

        calls.Should().BeEmpty();
        await harness.Stop();
    }
}
