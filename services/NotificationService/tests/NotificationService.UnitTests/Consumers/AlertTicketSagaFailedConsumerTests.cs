using MassTransit;
using MassTransit.Testing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.Consumers;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Services;
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
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<AlertTicketSagaFailedConsumer>();
                // Timeout tường minh — mặc định inactivity 1s của MassTransit v8 làm test đỏ
                // thất thường khi cả solution chạy song song. Xem ConsumerTestHarness.InactivityTimeout.
                x.SetTestTimeouts(Helpers.ConsumerTestHarness.TestTimeout, Helpers.ConsumerTestHarness.InactivityTimeout);
            })
            .AddSingleton(mediator)
            .AddSingleton<ITemplateRenderer, HandlebarsTemplateRenderer>()
            .AddSingleton(cache ?? ProceedCache())
            .AddSingleton(Resolver())
            .AddSingleton(NullLogger<AlertTicketSagaFailedConsumer>.Instance)
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return harness;
    }

    /// <summary>Resolver mock: trả về đúng 1 recipient cho mọi role (3 channel × 1 = 3 notification).</summary>
    private static IRecipientResolver Resolver()
    {
        var r = new Mock<IRecipientResolver>();
        r.Setup(x => x.GetActiveByRoleAsync(It.IsAny<CancellationToken>(), It.IsAny<string[]>()))
            .ReturnsAsync(new[] { Guid.NewGuid() });
        return r.Object;
    }

    /// <summary>Cache mock mặc định: GetAsync trả null → debounce cho phép xử lý (lần đầu).</summary>
    private static ICacheService ProceedCache()
    {
        var c = new Mock<ICacheService>();
        c.Setup(x => x.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        c.Setup(x => x.TrySetIfNotExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
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

        // Body của Push/InApp là câu cho người đọc nghiệp vụ: nêu hậu quả (cảnh báo chưa thành
        // ticket) chứ KHÔNG chứa tên stage nội bộ hay reason kỹ thuật. Hai thứ đó vẫn còn
        // nguyên trong PayloadJson (và trong template Email) để tra cứu.
        captured.Where(c => c.Channel != NotificationChannelEnum.Email).Should().AllSatisfy(c =>
        {
            c.Body.Should().Contain("failed to automatically create a ticket");
            c.Body.Should().NotContain("AlertLinkRequested");
            c.Body.Should().NotContain("Asset not found");
        });
        captured.Should().AllSatisfy(c => c.PayloadJson.Should().Contain("AlertLinkRequested"));
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
        cache.Setup(x => x.TrySetIfNotExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

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
        cache.Setup(x => x.TrySetIfNotExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var evt = MakeEvent();
        var harness = await StartHarness(mediator.Object, cache.Object);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<AlertTicketSagaFailedEvent>()).Should().BeTrue();

        // Sprint 6.3 NOTI3-09 (#709) — debounce chiếm key bằng 1 lệnh atomic SET NX EX.
        cache.Verify(x => x.TrySetIfNotExistsAsync(
            It.Is<string>(k => k == $"notif_debounce:{evt.AlertId}"),
            It.IsAny<string>(),
            It.Is<TimeSpan>(t => t == TimeSpan.FromMinutes(5)),
            It.IsAny<CancellationToken>()), Times.Once);

        await harness.Stop();
    }
}
