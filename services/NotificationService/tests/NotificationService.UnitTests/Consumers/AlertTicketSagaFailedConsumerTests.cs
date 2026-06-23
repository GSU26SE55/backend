using MassTransit;
using MassTransit.Testing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.Consumers;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Templates;
using NotificationService.Domain.Enums;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using SharedContracts.Saga.AlertTicket;

namespace NotificationService.UnitTests.Consumers;

/// <summary>
/// Sprint 5B #238 — AlertTicketSagaFailedConsumer publish 3 channels (Push/Email/InApp)
/// cho Admin reprocess required.
/// </summary>
public class AlertTicketSagaFailedConsumerTests
{
    private static async Task<ITestHarness> StartHarness(IMediator mediator, ICacheService? cache = null)
    {
        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x => x.AddConsumer<AlertTicketSagaFailedConsumer>())
            .AddSingleton(mediator)
            .AddSingleton<ITemplateRenderer, HandlebarsTemplateRenderer>()
            .AddSingleton(cache ?? ProceedCache())
            .AddSingleton(NullLogger<AlertTicketSagaFailedConsumer>.Instance)
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return harness;
    }

    /// <summary>Cache mock mặc định: GetAsync trả null → debounce cho phép xử lý (lần đầu).</summary>
    private static ICacheService ProceedCache()
    {
        var c = new Mock<ICacheService>();
        c.Setup(x => x.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        return c.Object;
    }

    private static AlertTicketSagaFailedEvent MakeEvent(string stage = "TicketRequested", Guid? ticketId = null) => new(
        CorrelationId: Guid.NewGuid(),
        AlertId: Guid.NewGuid(),
        TicketId: ticketId,
        BatteryAssetId: Guid.NewGuid(),
        CustomerId: Guid.NewGuid(),
        AssetSerialNumber: "BMS-F",
        FailedAtStage: stage,
        Reason: "Asset not found",
        ErrorCode: "ASSET_NOT_FOUND",
        FailedAt: DateTime.UtcNow);

    [Fact]
    public async Task Consume_ShouldDispatch_PushEmailInApp_Channels()
    {
        var mediator = new Mock<IMediator>();
        var calls = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) =>
                calls.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(MakeEvent());
        (await harness.Consumed.Any<AlertTicketSagaFailedEvent>()).Should().BeTrue();

        calls.Should().HaveCount(3);
        calls.Should().Contain(c => c.Channel == NotificationChannelEnum.Push);
        calls.Should().Contain(c => c.Channel == NotificationChannelEnum.Email);
        calls.Should().Contain(c => c.Channel == NotificationChannelEnum.InApp);
        calls.Should().AllSatisfy(c => c.Type.Should().Be(NotificationTypeEnum.AlertTicketSagaFailed));

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_ShouldSetEntityType_AlertTicketSaga()
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
        (await harness.Consumed.Any<AlertTicketSagaFailedEvent>()).Should().BeTrue();

        captured.Should().AllSatisfy(c =>
        {
            c.EntityType.Should().Be("AlertTicketSaga");
            c.EntityId.Should().Be(evt.AlertId);
        });

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_BodyShouldContain_StageAndReason()
    {
        var mediator = new Mock<IMediator>();
        var captured = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) =>
                captured.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(MakeEvent(stage: "AlertLinkRequested"));
        (await harness.Consumed.Any<AlertTicketSagaFailedEvent>()).Should().BeTrue();

        captured.Should().AllSatisfy(c =>
        {
            c.Body.Should().Contain("AlertLinkRequested");
            c.Body.Should().Contain("Asset not found");
        });
        await harness.Stop();
    }

    [Fact]
    public async Task Consume_PayloadShouldEmbedCorrelationAndTicketId()
    {
        var mediator = new Mock<IMediator>();
        var captured = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) =>
                captured.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var ticketId = Guid.NewGuid();
        var evt = MakeEvent(ticketId: ticketId);
        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<AlertTicketSagaFailedEvent>()).Should().BeTrue();

        captured.Should().AllSatisfy(c =>
        {
            c.PayloadJson.Should().Contain(evt.CorrelationId.ToString());
            c.PayloadJson.Should().Contain(ticketId.ToString());
            c.PayloadJson.Should().Contain("ASSET_NOT_FOUND");
        });

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_NullTicketId_ShouldStillWork()
    {
        var mediator = new Mock<IMediator>();
        var captured = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) =>
                captured.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var evt = MakeEvent(ticketId: null);
        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<AlertTicketSagaFailedEvent>()).Should().BeTrue();

        captured.Should().HaveCount(3);
        await harness.Stop();
    }

    [Fact]
    public async Task Consume_DuplicateAlertId_WithinWindow_ShouldSkip()
    {
        var mediator = new Mock<IMediator>();
        var calls = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) =>
                calls.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var cache = new Mock<ICacheService>();
        cache.Setup(x => x.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("2026-06-23T00:00:00.0000000Z");

        var harness = await StartHarness(mediator.Object, cache.Object);
        await harness.Bus.Publish(MakeEvent());
        (await harness.Consumed.Any<AlertTicketSagaFailedEvent>()).Should().BeTrue();

        calls.Should().BeEmpty();
        cache.Verify(x => x.SetAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_FirstEvent_ShouldSetDebounceKey_5Min()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var cache = new Mock<ICacheService>();
        cache.Setup(x => x.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var evt = MakeEvent();
        var harness = await StartHarness(mediator.Object, cache.Object);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<AlertTicketSagaFailedEvent>()).Should().BeTrue();

        cache.Verify(x => x.SetAsync(
            It.Is<string>(k => k == $"notif_debounce:{evt.AlertId}"),
            It.IsAny<string>(),
            It.Is<TimeSpan?>(t => t == TimeSpan.FromMinutes(5)),
            It.IsAny<CancellationToken>()), Times.Once);

        await harness.Stop();
    }
}
