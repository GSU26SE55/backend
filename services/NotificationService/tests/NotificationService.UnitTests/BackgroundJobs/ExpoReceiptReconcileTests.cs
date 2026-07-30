using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.BackgroundJobs;
using NotificationService.UnitTests.Helpers;
using NotificationEntity = NotificationService.Domain.Entities.Notification;

namespace NotificationService.UnitTests.BackgroundJobs;

/// <summary>
/// Sprint 6.3 NOTI3-02 (#702) + NOTI3-14 (#714) — đối soát biên nhận Expo.
///
/// Trọng tâm: phân biệt cho đúng ba trạng thái mà trước sprint này bị gộp làm một —
/// "Expo đã nhận" (Sent), "thiết bị đã nhận" (Delivered), và "không giao được" (Failed).
/// </summary>
public class ExpoReceiptReconcileTests
{
    private const string Token = "ExponentPushToken[abc]";

    /// <summary>
    /// Handler giả trả về một response cố định. Không tái dùng bản trong <c>ExpoPushChannelTests</c>
    /// vì bản đó là <c>private</c> nested — copy 10 dòng rẻ hơn là nới visibility của test khác.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StubHandler(HttpResponseMessage response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_response);
    }

    private static ExpoReceiptReconcileBackgroundService Build(
        Mock<INotificationUnitOfWork> uow,
        HttpResponseMessage response,
        ExpoReceiptOptions? options = null,
        INotificationAuditWriter? auditWriter = null)
    {
        var handler = new StubHandler(response);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://exp.host") };
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient("expo")).Returns(client);

        var services = new ServiceCollection();
        services.AddSingleton(uow.Object);
        if (auditWriter is not null)
            services.AddSingleton(auditWriter);

        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new ExpoReceiptReconcileBackgroundService(
            scopeFactory,
            httpFactory.Object,
            Options.Create(options ?? new ExpoReceiptOptions { MinAgeMinutes = 0 }),
            NullLogger<ExpoReceiptReconcileBackgroundService>.Instance);
    }

    private static HttpResponseMessage Json(object payload) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

    private static PushReceipt Pending(Guid notificationId, string ticketId = "ticket-1", string token = Token) => new()
    {
        Id = Guid.NewGuid(),
        NotificationId = notificationId,
        UserId = Guid.NewGuid(),
        TicketId = ticketId,
        DeviceToken = token,
        Status = PushReceiptStatusEnum.Pending,
        CreatedAt = DateTime.UtcNow.AddHours(-1),
    };

    private static NotificationEntity SentPush(Guid id) => new()
    {
        Id = id,
        UserId = Guid.NewGuid(),
        Channel = NotificationChannelEnum.Push,
        Type = NotificationTypeEnum.TicketAssigned,
        Status = NotificationStatusEnum.Sent,
        CreatedAt = DateTime.UtcNow.AddHours(-1),
    };

    [Fact]
    public async Task ReceiptOk_MarksReceiptOk_AndNotificationDelivered()
    {
        var notificationId = Guid.NewGuid();
        var receipt = Pending(notificationId);
        var notification = SentPush(notificationId);

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            notificationSeed: [notification], pushReceiptSeed: [receipt]);

        var sut = Build(uow, Json(new { data = new Dictionary<string, object> { ["ticket-1"] = new { status = "ok" } } }));

        var resolved = await sut.ReconcileOnceAsync(CancellationToken.None);

        resolved.Should().Be(1);
        receipt.Status.Should().Be(PushReceiptStatusEnum.Ok);
        receipt.CheckedAt.Should().NotBeNull();
        notification.Status.Should().Be(NotificationStatusEnum.Delivered,
            "receipt ok là bằng chứng giao hàng thật, mạnh hơn Sent");
    }

    /// <summary>Đây là lý do chính của cả task: token chết phải bị dọn, không thì gửi mãi vào hư không.</summary>
    [Fact]
    public async Task DeviceNotRegistered_DeactivatesDeviceToken()
    {
        var notificationId = Guid.NewGuid();
        var receipt = Pending(notificationId);
        var deviceToken = new DeviceToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Token = Token,
            IsActive = true
        };

        var (uow, deviceTokens, _) = MockNotificationUnitOfWork.Build(
            deviceTokenSeed: [deviceToken],
            notificationSeed: [SentPush(notificationId)],
            pushReceiptSeed: [receipt]);

        var sut = Build(uow, Json(new
        {
            data = new Dictionary<string, object>
            {
                ["ticket-1"] = new { status = "error", message = "not registered", details = new { error = "DeviceNotRegistered" } }
            }
        }));

        await sut.ReconcileOnceAsync(CancellationToken.None);

        receipt.Status.Should().Be(PushReceiptStatusEnum.Error);
        receipt.ErrorCode.Should().Be("DeviceNotRegistered");
        deviceToken.IsActive.Should().BeFalse();
        deviceTokens.Verify(r => r.UpdateAsync(deviceToken), Times.Once);
    }

    [Fact]
    public async Task ReceiptError_MarksNotificationFailed_WhenNoOtherDeviceSucceeded()
    {
        var notificationId = Guid.NewGuid();
        var notification = SentPush(notificationId);

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            notificationSeed: [notification],
            pushReceiptSeed: [Pending(notificationId)]);

        var sut = Build(uow, Json(new
        {
            data = new Dictionary<string, object>
            {
                ["ticket-1"] = new { status = "error", details = new { error = "MessageTooBig" } }
            }
        }));

        await sut.ReconcileOnceAsync(CancellationToken.None);

        notification.Status.Should().Be(NotificationStatusEnum.Failed);
        notification.FailureReason.Should().Contain("MessageTooBig");
    }

    /// <summary>
    /// User có 2 máy, 1 máy gỡ app. Đó KHÔNG phải thất bại giao hàng — notification vẫn tới nơi.
    /// </summary>
    [Fact]
    public async Task ReceiptError_DoesNotFailNotification_WhenAnotherDeviceDelivered()
    {
        var notificationId = Guid.NewGuid();
        var notification = SentPush(notificationId);

        var delivered = Pending(notificationId, "ticket-ok", "ExponentPushToken[ok]");
        delivered.Status = PushReceiptStatusEnum.Ok;

        var failing = Pending(notificationId, "ticket-bad", "ExponentPushToken[bad]");

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            notificationSeed: [notification],
            pushReceiptSeed: [delivered, failing]);

        var sut = Build(uow, Json(new
        {
            data = new Dictionary<string, object>
            {
                ["ticket-bad"] = new { status = "error", details = new { error = "DeviceNotRegistered" } }
            }
        }));

        await sut.ReconcileOnceAsync(CancellationToken.None);

        notification.Status.Should().Be(NotificationStatusEnum.Sent,
            "một thiết bị nhận được là đủ — không được đánh Failed");
    }

    /// <summary>User đã đọc rồi thì rõ ràng đã nhận — receipt về sau không được ghi đè.</summary>
    [Fact]
    public async Task ReceiptError_DoesNotDowngradeReadNotification()
    {
        var notificationId = Guid.NewGuid();
        var notification = SentPush(notificationId);
        notification.Status = NotificationStatusEnum.Read;

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            notificationSeed: [notification],
            pushReceiptSeed: [Pending(notificationId)]);

        var sut = Build(uow, Json(new
        {
            data = new Dictionary<string, object>
            {
                ["ticket-1"] = new { status = "error", details = new { error = "DeviceNotRegistered" } }
            }
        }));

        await sut.ReconcileOnceAsync(CancellationToken.None);

        notification.Status.Should().Be(NotificationStatusEnum.Read);
    }

    [Fact]
    public async Task ReceiptOk_DoesNotDowngradeOpenedNotification()
    {
        var notificationId = Guid.NewGuid();
        var notification = SentPush(notificationId);
        notification.Status = NotificationStatusEnum.Opened;

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            notificationSeed: [notification],
            pushReceiptSeed: [Pending(notificationId)]);

        var sut = Build(uow, Json(new { data = new Dictionary<string, object> { ["ticket-1"] = new { status = "ok" } } }));

        await sut.ReconcileOnceAsync(CancellationToken.None);

        notification.Status.Should().Be(NotificationStatusEnum.Opened);
    }

    /// <summary>Expo chưa có kết quả — "chưa biết" KHÔNG được biến thành "thất bại".</summary>
    [Fact]
    public async Task NoReceiptYet_KeepsPending_AndBumpsAttempt()
    {
        var notificationId = Guid.NewGuid();
        var receipt = Pending(notificationId);
        var notification = SentPush(notificationId);

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            notificationSeed: [notification], pushReceiptSeed: [receipt]);

        var sut = Build(uow, Json(new { data = new Dictionary<string, object>() }));

        var resolved = await sut.ReconcileOnceAsync(CancellationToken.None);

        resolved.Should().Be(0);
        receipt.Status.Should().Be(PushReceiptStatusEnum.Pending);
        receipt.CheckAttemptCount.Should().Be(1);
        notification.Status.Should().Be(NotificationStatusEnum.Sent);
    }

    /// <summary>Expo chỉ giữ receipt ~24h — hỏi mãi là lãng phí, phải bỏ cuộc.</summary>
    [Fact]
    public async Task NoReceipt_AfterMaxAttempts_MarksExpired()
    {
        var notificationId = Guid.NewGuid();
        var receipt = Pending(notificationId);
        receipt.CheckAttemptCount = 4;

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            notificationSeed: [SentPush(notificationId)], pushReceiptSeed: [receipt]);

        var sut = Build(uow,
            Json(new { data = new Dictionary<string, object>() }),
            new ExpoReceiptOptions { MinAgeMinutes = 0, MaxCheckAttempts = 5 });

        await sut.ReconcileOnceAsync(CancellationToken.None);

        receipt.Status.Should().Be(PushReceiptStatusEnum.Expired);
    }

    /// <summary>
    /// Mạng lỗi khác hẳn "Expo bảo thất bại". Gộp hai ca này sẽ khiến sự cố hạ tầng bị đếm
    /// thành thất bại giao hàng và làm sai toàn bộ số liệu.
    /// </summary>
    [Fact]
    public async Task HttpError_KeepsReceiptPending_DoesNotFailNotification()
    {
        var notificationId = Guid.NewGuid();
        var receipt = Pending(notificationId);
        var notification = SentPush(notificationId);

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            notificationSeed: [notification], pushReceiptSeed: [receipt]);

        var sut = Build(uow, new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var resolved = await sut.ReconcileOnceAsync(CancellationToken.None);

        resolved.Should().Be(0);
        receipt.Status.Should().Be(PushReceiptStatusEnum.Pending);
        notification.Status.Should().Be(NotificationStatusEnum.Sent);
    }

    /// <summary>Chưa đủ "chín" thì đừng hỏi — Expo gần như luôn trả rỗng, chỉ tốn request.</summary>
    [Fact]
    public async Task ReceiptYoungerThanMinAge_IsNotQueried()
    {
        var notificationId = Guid.NewGuid();
        var receipt = Pending(notificationId);
        receipt.CreatedAt = DateTime.UtcNow.AddMinutes(-1);

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            notificationSeed: [SentPush(notificationId)], pushReceiptSeed: [receipt]);

        var sut = Build(uow,
            Json(new { data = new Dictionary<string, object> { ["ticket-1"] = new { status = "ok" } } }),
            new ExpoReceiptOptions { MinAgeMinutes = 15 });

        var resolved = await sut.ReconcileOnceAsync(CancellationToken.None);

        resolved.Should().Be(0);
        receipt.Status.Should().Be(PushReceiptStatusEnum.Pending);
    }

    [Fact]
    public async Task AlreadyResolvedReceipt_IsNotQueriedAgain()
    {
        var notificationId = Guid.NewGuid();
        var receipt = Pending(notificationId);
        receipt.Status = PushReceiptStatusEnum.Ok;

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            notificationSeed: [SentPush(notificationId)], pushReceiptSeed: [receipt]);

        var sut = Build(uow, Json(new { data = new Dictionary<string, object>() }));

        (await sut.ReconcileOnceAsync(CancellationToken.None)).Should().Be(0);
        receipt.CheckAttemptCount.Should().Be(0);
    }

    /// <summary>Sprint 6.3 NOTI3-14 (#714) — receipt ok phải để lại dấu vết audit.</summary>
    [Fact]
    public async Task ReceiptOk_WritesPushDeliveredAudit()
    {
        var notificationId = Guid.NewGuid();
        var auditWriter = new Mock<INotificationAuditWriter>();
        auditWriter.Setup(w => w.WriteAsync(
                It.IsAny<NotificationAuditActionEnum>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<bool>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyDictionary<string, object?>?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            notificationSeed: [SentPush(notificationId)],
            pushReceiptSeed: [Pending(notificationId)]);

        var sut = Build(uow,
            Json(new { data = new Dictionary<string, object> { ["ticket-1"] = new { status = "ok" } } }),
            auditWriter: auditWriter.Object);

        await sut.ReconcileOnceAsync(CancellationToken.None);

        auditWriter.Verify(w => w.WriteAsync(
            NotificationAuditActionEnum.PushDelivered, notificationId, It.IsAny<Guid>(),
            true, null, It.IsAny<IReadOnlyDictionary<string, object?>?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task NothingPending_DoesNotCallExpo()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build();

        var httpFactory = new Mock<IHttpClientFactory>();
        var services = new ServiceCollection();
        services.AddSingleton(uow.Object);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var sut = new ExpoReceiptReconcileBackgroundService(
            scopeFactory, httpFactory.Object,
            Options.Create(new ExpoReceiptOptions { MinAgeMinutes = 0 }),
            NullLogger<ExpoReceiptReconcileBackgroundService>.Instance);

        (await sut.ReconcileOnceAsync(CancellationToken.None)).Should().Be(0);
        httpFactory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }
}
