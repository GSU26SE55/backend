using MassTransit;
using MassTransit.Testing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.Consumers;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Domain.Enums;
using SharedContracts.Common.Responses;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

/// <summary>
/// Sprint 5B #238 — BatteryAlertEscalationRequestedConsumer publishes Push + InApp
/// notifications cho escalation event.
/// </summary>
public class BatteryAlertEscalationRequestedConsumerTests
{
    private static async Task<ITestHarness> StartHarness(IMediator mediator)
    {
        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x => x.AddConsumer<BatteryAlertEscalationRequestedConsumer>())
            .AddSingleton(mediator)
            .AddSingleton(NullLogger<BatteryAlertEscalationRequestedConsumer>.Instance)
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return harness;
    }

    private static BatteryAlertEscalationRequestedEvent MakeEvent() => new(
        AlertId: Guid.NewGuid(),
        BatteryAssetId: Guid.NewGuid(),
        CustomerId: Guid.NewGuid(),
        AssetSerialNumber: "BMS-1",
        AnomalyType: 1, Severity: 3,
        ActualValue: 75m, Unit: "C",
        DetectedAt: DateTime.UtcNow.AddMinutes(-6),
        EscalationRequestedAt: DateTime.UtcNow,
        MinutesSinceDetection: 6);

    [Fact]
    public async Task Consume_ShouldDispatch_PushAndInAppNotifications()
    {
        var mediator = new Mock<IMediator>();
        var calls = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) =>
                calls.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true, StatusCode = 201 });

        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(MakeEvent());
        (await harness.Consumed.Any<BatteryAlertEscalationRequestedEvent>()).Should().BeTrue();

        calls.Should().HaveCount(2);
        calls.Should().Contain(c => c.Channel == NotificationChannelEnum.Push);
        calls.Should().Contain(c => c.Channel == NotificationChannelEnum.InApp);
        calls.Should().AllSatisfy(c =>
            c.Type.Should().Be(NotificationTypeEnum.BatteryAlertEscalationPending));

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_ShouldSetEntityTypeToAlert_WithAlertIdAsEntityId()
    {
        var mediator = new Mock<IMediator>();
        var captured = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) =>
                captured.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var evt = MakeEvent();
        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<BatteryAlertEscalationRequestedEvent>()).Should().BeTrue();

        captured.Should().AllSatisfy(c =>
        {
            c.EntityType.Should().Be("Alert");
            c.EntityId.Should().Be(evt.AlertId);
        });

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_ShouldEmbedMinutesSinceDetection_InTitle()
    {
        var mediator = new Mock<IMediator>();
        var captured = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) =>
                captured.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(MakeEvent());
        (await harness.Consumed.Any<BatteryAlertEscalationRequestedEvent>()).Should().BeTrue();

        captured.Should().AllSatisfy(c => c.Title.Should().Contain("6 phút"));
        await harness.Stop();
    }

    [Fact]
    public async Task Consume_PayloadShouldContain_AlertIdAndSeverity()
    {
        var mediator = new Mock<IMediator>();
        var captured = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) =>
                captured.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var evt = MakeEvent();
        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<BatteryAlertEscalationRequestedEvent>()).Should().BeTrue();

        captured.Should().AllSatisfy(c =>
        {
            c.PayloadJson.Should().Contain(evt.AlertId.ToString());
            c.PayloadJson.Should().Contain("severity");
        });
        await harness.Stop();
    }

    [Fact]
    public async Task Consume_MediatorReturnsFail_ShouldNotThrow()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationActionResponse
            {
                IsSuccess = false,
                StatusCode = 500,
                Message = "DB down"
            });

        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(MakeEvent());

        // Consumer logs warning but does not throw — message considered consumed.
        (await harness.Consumed.Any<BatteryAlertEscalationRequestedEvent>()).Should().BeTrue();

        await harness.Stop();
    }
}
