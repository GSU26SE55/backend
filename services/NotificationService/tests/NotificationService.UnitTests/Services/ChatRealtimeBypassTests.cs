using Microsoft.Extensions.Logging.Abstractions;
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

namespace NotificationService.UnitTests.Services;

/// <summary>
/// ADR-0019 — sự kiện hội thoại phải đi ngay, không được xếp hàng chờ.
///
/// <para>Từ khi kênh Push thành đường realtime chính của chat, ba cơ chế làm chậm sẵn có (quiet
/// hours, digest, hạn mức) đều biến "nhắn tin" thành "nhắn tin, mai nhận". Bộ test này khoá cả ba
/// nhánh miễn trừ, và khoá luôn mặt còn lại: loại thông báo bình thường vẫn phải bị hoãn như cũ.</para>
/// </summary>
public class ChatRealtimeBypassTests
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

    private static Notification Pending(NotificationTypeEnum type) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        Type = type,
        Channel = NotificationChannelEnum.Push,
        Status = NotificationStatusEnum.Pending,
        Title = "Tiêu đề",
        Body = "Nội dung",
        EntityType = "Chat",
    };

    private static AccountReadModel Account() => new()
    {
        Id = UserId,
        Email = "user@x.com",
        FullName = "User",
        PhoneNumber = "0901234567",
        Role = "Customer",
        IsActive = true,
    };

    /// <summary>
    /// Quiet hours phủ trọn 24 giờ để test không phụ thuộc vào lúc chạy. Trước đây dùng khung giờ
    /// cố định thì bộ test xanh hay đỏ tuỳ giờ chạy CI — đúng loại flaky khó lần nhất.
    /// </summary>
    private static NotificationPreference AlwaysQuiet() => new()
    {
        UserId = UserId,
        PushEnabled = true,
        EmailEnabled = true,
        SmsEnabled = true,
        InAppEnabled = true,
        TimeZone = "Asia/Ho_Chi_Minh",
        QuietHoursStart = new TimeOnly(0, 0),
        QuietHoursEnd = new TimeOnly(23, 59),
    };

    private static NotificationPreference DailyDigest() => new()
    {
        UserId = UserId,
        PushEnabled = true,
        EmailEnabled = true,
        SmsEnabled = true,
        InAppEnabled = true,
        TimeZone = "Asia/Ho_Chi_Minh",
        Frequency = NotificationFrequencyEnum.Daily,
    };

    private static NotificationDispatcher Build(
        Notification notification,
        NotificationPreference pref,
        INotificationRateLimiter? rateLimiter = null)
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            notificationSeed: [notification],
            accountSeed: [Account()]);

        var prefRepo = new Mock<IGenericRepository<NotificationPreference>>();
        prefRepo.Setup(r => r.GetAllAsync()).Returns(new[] { pref }.AsQueryable().BuildMock());
        uow.SetupGet(u => u.NotificationPreferences).Returns(prefRepo.Object);

        return new NotificationDispatcher(
            uow.Object,
            NoCache().Object,
            [Channel(NotificationChannelEnum.Push).Object],
            new Mock<ITemplateRenderer>().Object,
            new NoopAuditWriter(),
            Microsoft.Extensions.Options.Options.Create(new NotificationDispatchOptions()),
            NullLogger<NotificationDispatcher>.Instance,
            rateLimiter,
            Microsoft.Extensions.Options.Options.Create(new NotificationRateLimitOptions()));
    }

    // ════════════════════════ Quiet hours ════════════════════════

    [Theory]
    [InlineData(NotificationTypeEnum.ChatCreated)]
    [InlineData(NotificationTypeEnum.ChatMentioned)]
    public async Task ChatKhongBiHoanBoiQuietHours(NotificationTypeEnum type)
    {
        var n = Pending(type);
        var sut = Build(n, AlwaysQuiet());

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Sent);
        n.Status.Should().Be(NotificationStatusEnum.Sent);
    }

    [Fact]
    public async Task LoaiThongThuongVanBiHoanBoiQuietHours()
    {
        // Mặt còn lại của cùng một luật: miễn trừ chỉ áp cho hội thoại, không nới cho cả hệ thống.
        var n = Pending(NotificationTypeEnum.TicketCreated);
        var sut = Build(n, AlwaysQuiet());

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Deferred);
        n.NextAttemptAt.Should().NotBeNull();
        n.Status.Should().Be(NotificationStatusEnum.Pending);
    }

    // ════════════════════════ Digest ════════════════════════

    [Theory]
    [InlineData(NotificationTypeEnum.ChatCreated)]
    [InlineData(NotificationTypeEnum.ChatMentioned)]
    public async Task ChatKhongBiGomVaoDigest(NotificationTypeEnum type)
    {
        var n = Pending(type);
        var sut = Build(n, DailyDigest());

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Sent);
    }

    [Fact]
    public async Task LoaiThongThuongVanBiGomVaoDigest()
    {
        var n = Pending(NotificationTypeEnum.TicketCreated);
        var sut = Build(n, DailyDigest());

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Deferred);
    }

    // ════════════════════════ Hạn mức ════════════════════════

    [Theory]
    [InlineData(NotificationTypeEnum.ChatCreated)]
    [InlineData(NotificationTypeEnum.ChatMentioned)]
    public async Task ChatKhongHoiHanMuc(NotificationTypeEnum type)
    {
        var limiter = new Mock<INotificationRateLimiter>();
        limiter.Setup(x => x.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<NotificationTypeEnum>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new RateLimitDecision(false, "per_hour"));

        var n = Pending(type);
        var sut = Build(n, new NotificationPreference { UserId = UserId, PushEnabled = true, TimeZone = "Asia/Ho_Chi_Minh" }, limiter.Object);

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Sent);

        // Không chỉ "không bị hoãn" — phải KHÔNG HỎI hạn mức, nếu không mỗi tin nhắn vẫn ăn một
        // slot của người dùng rồi làm cạn hạn mức của các thông báo khác.
        limiter.Verify(
            x => x.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<NotificationTypeEnum>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LoaiThongThuongVanBiHanMucChan()
    {
        var limiter = new Mock<INotificationRateLimiter>();
        limiter.Setup(x => x.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<NotificationTypeEnum>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new RateLimitDecision(false, "per_hour"));

        var n = Pending(NotificationTypeEnum.TicketCreated);
        var sut = Build(n, new NotificationPreference { UserId = UserId, PushEnabled = true, TimeZone = "Asia/Ho_Chi_Minh" }, limiter.Object);

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Deferred);
    }
}
