using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Application.Templates;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Channels;
using NotificationService.Infrastructure.Services;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Interfaces;
using SharedKernels.Interfaces;
using NotificationEntity = NotificationService.Domain.Entities.Notification;

namespace NotificationService.UnitTests.Services;

/// <summary>
/// Sprint 6.3 NOTI3-06 (#706) — hạn mức nối vào <c>DispatchPendingAsync</c>.
///
/// Điểm mấu chốt: vượt trần thì **HOÃN** (Pending + NextAttemptAt) để digest gộp lại,
/// KHÔNG đánh Failed và KHÔNG vứt bỏ — vứt notification là mất dữ liệu nghiệp vụ.
/// </summary>
public class DispatchRateLimitTests
{
    private static readonly Guid UserId = Guid.Parse("cccccccc-1111-2222-3333-444444444444");

    private static Mock<ICacheService> NoCache()
    {
        var m = new Mock<ICacheService>();
        m.Setup(c => c.GetAsync<NotificationPreference>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync((NotificationPreference?)null);
        return m;
    }

    private static Mock<INotificationChannel> Channel(NotificationChannelEnum type)
    {
        var m = new Mock<INotificationChannel>();
        m.SetupGet(c => c.ChannelType).Returns(type);
        m.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync(new ChannelResult(true));
        return m;
    }

    private static NotificationEntity Pending(
        NotificationChannelEnum channel,
        NotificationTypeEnum type = NotificationTypeEnum.TicketCreated,
        string? entityType = "Ticket") => new()
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Type = type,
            Channel = channel,
            Status = NotificationStatusEnum.Pending,
            Title = "T",
            Body = "B",
            EntityType = entityType,
        };

    private static (NotificationDispatcher sut, Mock<INotificationChannel> channel) Build(
        NotificationEntity notification,
        INotificationRateLimiter? limiter,
        NotificationRateLimitOptions? rateOptions = null)
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            deviceTokenSeed: [new DeviceToken { Id = Guid.NewGuid(), UserId = UserId, Token = "ExponentPushToken[x]", IsActive = true }],
            notificationSeed: [notification],
            accountSeed: [new AccountReadModel
            {
                Id = UserId, Email = "u@x.com", FullName = "U", PhoneNumber = "0901234567",
                Role = "Customer", IsActive = true,
            }]);

        var prefRepo = new Mock<IGenericRepository<NotificationPreference>>();
        prefRepo.Setup(r => r.GetAllAsync())
                .Returns(Array.Empty<NotificationPreference>().AsQueryable().BuildMock());
        uow.SetupGet(u => u.NotificationPreferences).Returns(prefRepo.Object);

        var channel = Channel(notification.Channel);

        var sut = new NotificationDispatcher(
            uow.Object,
            NoCache().Object,
            [channel.Object],
            new Mock<ITemplateRenderer>().Object,
            new NoopAuditWriter(),
            Options.Create(new NotificationDispatchOptions()),
            NullLogger<NotificationDispatcher>.Instance,
            limiter,
            Options.Create(rateOptions ?? new NotificationRateLimitOptions { DeferMinutes = 60 }));

        return (sut, channel);
    }

    private static INotificationRateLimiter Limiter(bool allowed, string? reason = "per_hour")
    {
        var m = new Mock<INotificationRateLimiter>();
        m.Setup(l => l.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<NotificationTypeEnum>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync(new RateLimitDecision(allowed, reason));
        return m.Object;
    }

    [Fact]
    public async Task OverLimit_DefersInsteadOfFailing()
    {
        var n = Pending(NotificationChannelEnum.Push);
        var (sut, channel) = Build(n, Limiter(allowed: false));

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Deferred);
        n.Status.Should().Be(NotificationStatusEnum.Pending, "hoãn chứ không vứt");
        n.NextAttemptAt.Should().NotBeNull();
        n.NextAttemptAt!.Value.Should().BeAfter(DateTime.UtcNow.AddMinutes(50));

        channel.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnderLimit_SendsNormally()
    {
        var n = Pending(NotificationChannelEnum.Push);
        var (sut, channel) = Build(n, Limiter(allowed: true, reason: null));

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Sent);
        channel.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Cảnh báo an toàn KHÔNG bao giờ được hoãn vì hạn mức — đây là điều kiện tiên quyết,
    /// không phải tuỳ chọn. <c>SlaBreached</c> nằm trong <c>DefaultCriticalTypes</c>.
    /// </summary>
    [Fact]
    public async Task CriticalType_BypassesRateLimit()
    {
        var n = Pending(NotificationChannelEnum.Push, NotificationTypeEnum.SlaBreached);
        var limiter = new Mock<INotificationRateLimiter>();
        limiter.Setup(l => l.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<NotificationTypeEnum>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new RateLimitDecision(false, "per_hour"));

        var (sut, channel) = Build(n, limiter.Object);

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Sent);
        channel.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        limiter.Verify(l => l.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<NotificationTypeEnum>(), It.IsAny<CancellationToken>()),
            Times.Never, "critical thì không cần hỏi hạn mức");
    }

    /// <summary>Feed in-app không làm phiền ai — giới hạn nó chỉ khiến user mất dữ liệu trên màn hình.</summary>
    [Fact]
    public async Task InAppChannel_IsNeverRateLimited()
    {
        var n = Pending(NotificationChannelEnum.InApp);
        var limiter = new Mock<INotificationRateLimiter>();
        limiter.Setup(l => l.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<NotificationTypeEnum>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new RateLimitDecision(false, "per_hour"));

        var (sut, channel) = Build(n, limiter.Object);

        (await sut.DispatchPendingAsync(n)).Should().Be(DispatchOutcome.Sent);
        channel.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        limiter.Verify(l => l.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<NotificationTypeEnum>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>Bản digest tổng hợp mà bị hạn mức hoãn tiếp thì sẽ tự hoãn chính nó mãi mãi.</summary>
    [Fact]
    public async Task DigestNotification_IsNotRateLimited()
    {
        var n = Pending(NotificationChannelEnum.Email, entityType: NotificationDigest.EntityType);
        var limiter = new Mock<INotificationRateLimiter>();
        limiter.Setup(l => l.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<NotificationTypeEnum>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new RateLimitDecision(false, "per_hour"));

        var (sut, channel) = Build(n, limiter.Object);

        (await sut.DispatchPendingAsync(n)).Should().Be(DispatchOutcome.Sent);
        channel.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Không cấu hình limiter (caller cũ) ⇒ hành vi y hệt trước sprint này.</summary>
    [Fact]
    public async Task NoLimiterConfigured_BehavesAsBefore()
    {
        var n = Pending(NotificationChannelEnum.Email);
        var (sut, channel) = Build(n, limiter: null);

        (await sut.DispatchPendingAsync(n)).Should().Be(DispatchOutcome.Sent);
        channel.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
