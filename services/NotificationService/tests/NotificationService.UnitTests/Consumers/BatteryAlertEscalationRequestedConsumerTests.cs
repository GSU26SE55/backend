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
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.UnitTests.Consumers;

/// <summary>
/// Sprint 5B #238 — BatteryAlertEscalationRequestedConsumer publishes Push + Email + InApp
/// notifications cho escalation event (overall.md §3.4).
/// </summary>
public class BatteryAlertEscalationRequestedConsumerTests
{
    private static async Task<ITestHarness> StartHarness(IMediator mediator, ICacheService? cache = null)
    {
        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<BatteryAlertEscalationRequestedConsumer>();
                // Timeout tường minh — mặc định inactivity 1s của MassTransit v8 làm test đỏ
                // thất thường khi cả solution chạy song song. Xem ConsumerTestHarness.InactivityTimeout.
                x.SetTestTimeouts(Helpers.ConsumerTestHarness.TestTimeout, Helpers.ConsumerTestHarness.InactivityTimeout);
            })
            .AddSingleton(mediator)
            .AddSingleton<ITemplateRenderer, HandlebarsTemplateRenderer>()
            .AddSingleton(cache ?? ProceedCache())
            .AddSingleton(Resolver())
            .AddSingleton(NullLogger<BatteryAlertEscalationRequestedConsumer>.Instance)
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
    public async Task Consume_ShouldDispatch_PushEmailInAppNotifications()
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

        calls.Should().HaveCount(3);
        calls.Should().Contain(c => c.Channel == NotificationChannelEnum.Push);
        calls.Should().Contain(c => c.Channel == NotificationChannelEnum.Email);
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

        // Số phút chưa tiếp nhận chuyển từ tiêu đề xuống body: tiêu đề nêu việc gì đang xảy ra
        // (pin nào chưa được xử lý), phần "đã bao lâu" thuộc về phần diễn giải.
        // Email dùng template HTML riêng nên chỉ xét Push/InApp.
        captured.Where(c => c.Channel != NotificationChannelEnum.Email).Should()
            .AllSatisfy(c => c.Body.Should().Contain("6 minutes"));
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

    [Fact]
    public async Task Consume_DuplicateAlertId_WithinWindow_ShouldSkip()
    {
        var mediator = new Mock<IMediator>();
        var calls = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) =>
                calls.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        // Debounce key đã tồn tại → consumer phải skip.
        var cache = new Mock<ICacheService>();
        cache.Setup(x => x.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("2026-06-23T00:00:00.0000000Z");
        cache.Setup(x => x.TrySetIfNotExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var harness = await StartHarness(mediator.Object, cache.Object);
        await harness.Bus.Publish(MakeEvent());
        (await harness.Consumed.Any<BatteryAlertEscalationRequestedEvent>()).Should().BeTrue();

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
        (await harness.Consumed.Any<BatteryAlertEscalationRequestedEvent>()).Should().BeTrue();

        // Sprint 6.3 NOTI3-09 (#709) — debounce chiếm key bằng 1 lệnh atomic SET NX EX.
        cache.Verify(x => x.TrySetIfNotExistsAsync(
            It.Is<string>(k => k == $"notif_debounce:{evt.AlertId}"),
            It.IsAny<string>(),
            It.Is<TimeSpan>(t => t == TimeSpan.FromMinutes(5)),
            It.IsAny<CancellationToken>()), Times.Once);

        await harness.Stop();
    }
}
