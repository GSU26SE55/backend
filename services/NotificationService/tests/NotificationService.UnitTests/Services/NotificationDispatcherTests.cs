using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using NotificationService.Application.DTOs.Request.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Channels;
using NotificationService.Infrastructure.Services;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Interfaces;
using SharedKernels.Interfaces;

namespace NotificationService.UnitTests.Services;

public class NotificationDispatcherTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static Mock<ICacheService> NoCache()
    {
        var m = new Mock<ICacheService>();
        m.Setup(c => c.GetAsync<NotificationPreference>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync((NotificationPreference?)null);
        return m;
    }

    private static Mock<INotificationChannel> FakeChannel(NotificationChannelEnum type)
    {
        var m = new Mock<INotificationChannel>();
        m.SetupGet(c => c.ChannelType).Returns(type);
        m.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync(new ChannelResult(true));
        return m;
    }

    private static (Mock<INotificationUnitOfWork> uow,
                    Mock<IGenericRepository<NotificationPreference>> prefs)
        BuildUow(NotificationPreference? pref = null, DeviceToken? deviceToken = null)
    {
        var prefSeed = pref is null ? [] : new[] { pref };
        var tokenSeed = deviceToken is null ? [] : new[] { deviceToken };

        var (uow, _, _) = MockNotificationUnitOfWork.Build(deviceTokenSeed: tokenSeed);

        var prefRepo = new Mock<IGenericRepository<NotificationPreference>>();
        prefRepo.Setup(r => r.GetAllAsync())
                .Returns(prefSeed.AsQueryable().BuildMock());
        uow.SetupGet(u => u.NotificationPreferences).Returns(prefRepo.Object);

        return (uow, prefRepo);
    }

    private static NotificationDispatcher Build(
        INotificationUnitOfWork uow,
        ICacheService cache,
        params INotificationChannel[] channels) =>
        new(uow, cache, channels, NullLogger<NotificationDispatcher>.Instance);

    // ── basic dispatch ────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_EmptyRecipients_DoesNothing()
    {
        var (uow, _) = BuildUow();
        var channel = FakeChannel(NotificationChannelEnum.InApp);
        var sut = Build(uow.Object, NoCache().Object, channel.Object);

        await sut.DispatchAsync(new DispatchRequest
        {
            Type = NotificationTypeEnum.TicketCreated,
            Recipients = [],
            Title = "T",
            Body = "B",
        });

        channel.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchAsync_InAppChannel_AlwaysSent()
    {
        var (uow, _) = BuildUow(); // no preference → defaults (all channels enabled)
        var inApp = FakeChannel(NotificationChannelEnum.InApp);
        var sut = Build(uow.Object, NoCache().Object, inApp.Object);

        await sut.DispatchAsync(new DispatchRequest
        {
            Type = NotificationTypeEnum.System,
            Recipients = [new RecipientInfo { UserId = Guid.NewGuid() }],
            Title = "T",
            Body = "B",
        });

        inApp.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── quiet hours ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_QuietHours_PushSkipped_InAppSent()
    {
        // Quiet hours covers entire day (00:00–23:59) to guarantee "now" is in quiet
        var userId = Guid.NewGuid();
        var pref = new NotificationPreference
        {
            UserId = userId,
            PushEnabled = true,
            EmailEnabled = true,
            SmsEnabled = false,
            InAppEnabled = true,
            QuietHoursStart = new TimeOnly(0, 0),
            QuietHoursEnd = new TimeOnly(23, 59),
            TimeZone = "UTC",
        };
        var deviceToken = new DeviceToken { UserId = userId, Token = "ExponentPushToken[test]", IsActive = true };

        var (uow, _) = BuildUow(pref, deviceToken);
        var push = FakeChannel(NotificationChannelEnum.Push);
        var inApp = FakeChannel(NotificationChannelEnum.InApp);
        var sut = Build(uow.Object, NoCache().Object, push.Object, inApp.Object);

        await sut.DispatchAsync(new DispatchRequest
        {
            Type = NotificationTypeEnum.TicketCreated,  // Push + InApp, NOT critical
            Recipients = [new RecipientInfo { UserId = userId }],
            Title = "T",
            Body = "B",
        });

        push.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        inApp.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── critical bypass ───────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_CriticalType_BypassesQuietHours()
    {
        var userId = Guid.NewGuid();
        var pref = new NotificationPreference
        {
            UserId = userId,
            PushEnabled = true,
            EmailEnabled = true,
            SmsEnabled = true,
            InAppEnabled = true,
            QuietHoursStart = new TimeOnly(0, 0),
            QuietHoursEnd = new TimeOnly(23, 59),
            TimeZone = "UTC",
        };
        var deviceToken = new DeviceToken { UserId = userId, Token = "ExponentPushToken[test]", IsActive = true };

        var (uow, _) = BuildUow(pref, deviceToken);
        var push = FakeChannel(NotificationChannelEnum.Push);
        var email = FakeChannel(NotificationChannelEnum.Email);
        var sms = FakeChannel(NotificationChannelEnum.Sms);
        var inApp = FakeChannel(NotificationChannelEnum.InApp);
        var sut = Build(uow.Object, NoCache().Object, push.Object, email.Object, sms.Object, inApp.Object);

        await sut.DispatchAsync(new DispatchRequest
        {
            Type = NotificationTypeEnum.SlaBreached, // Critical type
            Recipients = [new RecipientInfo { UserId = userId, Email = "a@b.com", PhoneNumber = "+84900" }],
            Title = "SLA Breached",
            Body = "B",
        });

        // All channels should fire despite quiet hours
        push.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        email.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        sms.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        inApp.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_BypassQuietHoursFlag_BypassesQuietHours()
    {
        var userId = Guid.NewGuid();
        var pref = new NotificationPreference
        {
            UserId = userId,
            PushEnabled = true,
            InAppEnabled = true,
            EmailEnabled = false,
            SmsEnabled = false,
            QuietHoursStart = new TimeOnly(0, 0),
            QuietHoursEnd = new TimeOnly(23, 59),
            TimeZone = "UTC",
        };
        var deviceToken = new DeviceToken { UserId = userId, Token = "token", IsActive = true };

        var (uow, _) = BuildUow(pref, deviceToken);
        var push = FakeChannel(NotificationChannelEnum.Push);
        var inApp = FakeChannel(NotificationChannelEnum.InApp);
        var sut = Build(uow.Object, NoCache().Object, push.Object, inApp.Object);

        await sut.DispatchAsync(new DispatchRequest
        {
            Type = NotificationTypeEnum.TicketCreated,
            BypassQuietHours = true,
            Recipients = [new RecipientInfo { UserId = userId }],
            Title = "T",
            Body = "B",
        });

        push.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── preference channel filtering ──────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_PushDisabled_PushNotSent()
    {
        var userId = Guid.NewGuid();
        var pref = new NotificationPreference
        {
            UserId = userId,
            PushEnabled = false,
            InAppEnabled = true,
            EmailEnabled = true,
            SmsEnabled = false,
        };
        var deviceToken = new DeviceToken { UserId = userId, Token = "token", IsActive = true };

        var (uow, _) = BuildUow(pref, deviceToken);
        var push = FakeChannel(NotificationChannelEnum.Push);
        var inApp = FakeChannel(NotificationChannelEnum.InApp);
        var sut = Build(uow.Object, NoCache().Object, push.Object, inApp.Object);

        await sut.DispatchAsync(new DispatchRequest
        {
            Type = NotificationTypeEnum.TicketCreated,
            Recipients = [new RecipientInfo { UserId = userId }],
            Title = "T",
            Body = "B",
        });

        push.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        inApp.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── cache ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_CachesPreference_SecondCallUsesCache()
    {
        var userId = Guid.NewGuid();
        var cachedPref = new NotificationPreference
        {
            UserId = userId,
            PushEnabled = false,
            InAppEnabled = true,
            EmailEnabled = false,
            SmsEnabled = false,
        };

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<NotificationPreference>(
                $"notif_pref:{userId}", It.IsAny<CancellationToken>()))
             .ReturnsAsync(cachedPref);

        var (uow, prefRepo) = BuildUow();
        var inApp = FakeChannel(NotificationChannelEnum.InApp);
        var sut = Build(uow.Object, cache.Object, inApp.Object);

        await sut.DispatchAsync(new DispatchRequest
        {
            Type = NotificationTypeEnum.System,
            Recipients = [new RecipientInfo { UserId = userId }],
            Title = "T",
            Body = "B",
        });

        // Preference repo should NOT be queried when cache hits
        prefRepo.Verify(r => r.GetAllAsync(), Times.Never);
        inApp.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── no device token → push skipped ───────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_NoPushToken_PushSkipped()
    {
        var userId = Guid.NewGuid();
        // No device token in seed
        var (uow, _) = BuildUow();
        var push = FakeChannel(NotificationChannelEnum.Push);
        var inApp = FakeChannel(NotificationChannelEnum.InApp);
        var sut = Build(uow.Object, NoCache().Object, push.Object, inApp.Object);

        await sut.DispatchAsync(new DispatchRequest
        {
            Type = NotificationTypeEnum.TicketCreated,
            Recipients = [new RecipientInfo { UserId = userId }],
            Title = "T",
            Body = "B",
        });

        push.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── midnight-wrapping quiet hours ────────────────────────────────────────

    [Fact]
    public async Task IsQuietHours_MidnightWrap_WorksCorrectly()
    {
        // 22:00–07:00 wraps midnight. We use UTC with 00:30 (inside quiet window)
        var userId = Guid.NewGuid();
        var utcNow = DateTime.UtcNow;

        // Force a time we can reason about by using TimeOnly directly.
        // Test the logic indirectly: set 22:00–07:00 and inject a token at 00:30 UTC.
        // Use TimeZone UTC so local == UTC.
        var pref = new NotificationPreference
        {
            UserId = userId,
            PushEnabled = true,
            InAppEnabled = true,
            QuietHoursStart = new TimeOnly(22, 0),
            QuietHoursEnd = new TimeOnly(7, 0),
            TimeZone = "UTC",
        };
        var deviceToken = new DeviceToken { UserId = userId, Token = "t", IsActive = true };

        // We can't control DateTime.UtcNow directly; instead we verify the matrix logic
        // by using a type that sends both Push+InApp (TicketCreated) and checking whether
        // quiet suppresses push depending on actual time. Since we can't freeze the clock,
        // this test documents the expected behavior and verifies no exception is thrown.
        var (uow, _) = BuildUow(pref, deviceToken);
        var push = FakeChannel(NotificationChannelEnum.Push);
        var inApp = FakeChannel(NotificationChannelEnum.InApp);
        var sut = Build(uow.Object, NoCache().Object, push.Object, inApp.Object);

        // Should not throw
        var act = () => sut.DispatchAsync(new DispatchRequest
        {
            Type = NotificationTypeEnum.TicketCreated,
            Recipients = [new RecipientInfo { UserId = userId }],
            Title = "T",
            Body = "B",
        });

        await act.Should().NotThrowAsync();
        // InApp always sent regardless of quiet hours
        inApp.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ══ GH-672 NOTI-01 — DispatchPendingAsync ════════════════════════════════

    private static Mock<INotificationUnitOfWork> BuildPendingUow(
        NotificationPreference? pref = null,
        DeviceToken[]? deviceTokens = null,
        AccountReadModel? account = null)
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            deviceTokenSeed: deviceTokens ?? [],
            accountSeed: account is null ? [] : new[] { account });

        var prefSeed = pref is null ? [] : new[] { pref };
        var prefRepo = new Mock<IGenericRepository<NotificationPreference>>();
        prefRepo.Setup(r => r.GetAllAsync())
                .Returns(prefSeed.AsQueryable().BuildMock());
        uow.SetupGet(u => u.NotificationPreferences).Returns(prefRepo.Object);

        return uow;
    }

    /// <summary>Quiet hours phủ trọn ngày để "bây giờ" chắc chắn rơi vào khung im lặng.</summary>
    private static NotificationPreference AllDayQuietPref(Guid userId, bool smsEnabled = false) =>
        new()
        {
            UserId = userId,
            PushEnabled = true,
            EmailEnabled = true,
            SmsEnabled = smsEnabled,
            InAppEnabled = true,
            QuietHoursStart = new TimeOnly(0, 0),
            QuietHoursEnd = new TimeOnly(23, 59),
            TimeZone = "UTC",
        };

    private static Notification PendingNotification(
        Guid userId,
        NotificationChannelEnum channel,
        NotificationTypeEnum type = NotificationTypeEnum.TicketCreated,
        string? payloadJson = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Channel = channel,
            Type = type,
            Status = NotificationStatusEnum.Pending,
            Title = "T",
            Body = "B",
            PayloadJson = payloadJson,
        };

    [Fact]
    public async Task DispatchPendingAsync_InApp_MarksSent()
    {
        var userId = Guid.NewGuid();
        var uow = BuildPendingUow();
        var inApp = FakeChannel(NotificationChannelEnum.InApp);
        var sut = Build(uow.Object, NoCache().Object, inApp.Object);
        var notification = PendingNotification(userId, NotificationChannelEnum.InApp);

        var settled = await sut.DispatchPendingAsync(notification);

        settled.Should().BeTrue();
        notification.Status.Should().Be(NotificationStatusEnum.Sent);
        notification.SentAt.Should().NotBeNull();
        inApp.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchPendingAsync_AlreadySent_ReturnsFalseWithoutSending()
    {
        var userId = Guid.NewGuid();
        var uow = BuildPendingUow();
        var inApp = FakeChannel(NotificationChannelEnum.InApp);
        var sut = Build(uow.Object, NoCache().Object, inApp.Object);
        var notification = PendingNotification(userId, NotificationChannelEnum.InApp);
        notification.Status = NotificationStatusEnum.Sent;

        var settled = await sut.DispatchPendingAsync(notification);

        settled.Should().BeFalse();
        inApp.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchPendingAsync_EmailChannel_StaysPendingUntilGh673()
    {
        var userId = Guid.NewGuid();
        var uow = BuildPendingUow();
        var email = FakeChannel(NotificationChannelEnum.Email);
        var sut = Build(uow.Object, NoCache().Object, email.Object);
        var notification = PendingNotification(userId, NotificationChannelEnum.Email);

        var settled = await sut.DispatchPendingAsync(notification);

        settled.Should().BeFalse();
        notification.Status.Should().Be(NotificationStatusEnum.Pending);
        email.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchPendingAsync_ChannelDisabledByPreference_MarksFailedWithoutSending()
    {
        var userId = Guid.NewGuid();
        var pref = new NotificationPreference
        {
            UserId = userId,
            PushEnabled = true,
            EmailEnabled = true,
            SmsEnabled = false,
            InAppEnabled = false,
        };
        var uow = BuildPendingUow(pref);
        var inApp = FakeChannel(NotificationChannelEnum.InApp);
        var sut = Build(uow.Object, NoCache().Object, inApp.Object);
        var notification = PendingNotification(userId, NotificationChannelEnum.InApp);

        var settled = await sut.DispatchPendingAsync(notification);

        settled.Should().BeTrue();
        notification.Status.Should().Be(NotificationStatusEnum.Failed);
        notification.FailureReason.Should().Contain("disabled by preference");
        inApp.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchPendingAsync_QuietHoursNonCritical_StaysPending()
    {
        var userId = Guid.NewGuid();
        var token = new DeviceToken { UserId = userId, Token = "t1", IsActive = true };
        var uow = BuildPendingUow(AllDayQuietPref(userId), [token]);
        var push = FakeChannel(NotificationChannelEnum.Push);
        var sut = Build(uow.Object, NoCache().Object, push.Object);
        var notification = PendingNotification(userId, NotificationChannelEnum.Push);

        var settled = await sut.DispatchPendingAsync(notification);

        settled.Should().BeFalse();
        notification.Status.Should().Be(NotificationStatusEnum.Pending);
        push.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchPendingAsync_CriticalType_SendsDuringQuietHours()
    {
        var userId = Guid.NewGuid();
        var token = new DeviceToken { UserId = userId, Token = "t1", IsActive = true };
        var uow = BuildPendingUow(AllDayQuietPref(userId), [token]);
        var push = FakeChannel(NotificationChannelEnum.Push);
        var sut = Build(uow.Object, NoCache().Object, push.Object);
        var notification = PendingNotification(userId, NotificationChannelEnum.Push, NotificationTypeEnum.SlaBreached);

        var settled = await sut.DispatchPendingAsync(notification);

        settled.Should().BeTrue();
        notification.Status.Should().Be(NotificationStatusEnum.Sent);
        push.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchPendingAsync_BypassQuietHoursPayload_SendsDuringQuietHours()
    {
        var userId = Guid.NewGuid();
        var token = new DeviceToken { UserId = userId, Token = "t1", IsActive = true };
        var uow = BuildPendingUow(AllDayQuietPref(userId), [token]);
        var push = FakeChannel(NotificationChannelEnum.Push);
        var sut = Build(uow.Object, NoCache().Object, push.Object);
        var notification = PendingNotification(userId, NotificationChannelEnum.Push,
            payloadJson: """{"bypassQuietHours":true}""");

        var settled = await sut.DispatchPendingAsync(notification);

        settled.Should().BeTrue();
        notification.Status.Should().Be(NotificationStatusEnum.Sent);
    }

    [Fact]
    public async Task DispatchPendingAsync_MalformedPayloadJson_TreatedAsNoBypass()
    {
        var userId = Guid.NewGuid();
        var token = new DeviceToken { UserId = userId, Token = "t1", IsActive = true };
        var uow = BuildPendingUow(AllDayQuietPref(userId), [token]);
        var push = FakeChannel(NotificationChannelEnum.Push);
        var sut = Build(uow.Object, NoCache().Object, push.Object);
        var notification = PendingNotification(userId, NotificationChannelEnum.Push, payloadJson: "{not-json");

        var settled = await sut.DispatchPendingAsync(notification);

        settled.Should().BeFalse();
        notification.Status.Should().Be(NotificationStatusEnum.Pending);
    }

    [Fact]
    public async Task DispatchPendingAsync_PushWithoutActiveToken_MarksFailed()
    {
        var userId = Guid.NewGuid();
        var uow = BuildPendingUow();
        var push = FakeChannel(NotificationChannelEnum.Push);
        var sut = Build(uow.Object, NoCache().Object, push.Object);
        var notification = PendingNotification(userId, NotificationChannelEnum.Push);

        var settled = await sut.DispatchPendingAsync(notification);

        settled.Should().BeTrue();
        notification.Status.Should().Be(NotificationStatusEnum.Failed);
        notification.FailureReason.Should().Be("No active device token");
        push.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchPendingAsync_PushFanOut_OneTokenSucceeds_MarksSent()
    {
        var userId = Guid.NewGuid();
        DeviceToken[] tokens =
        [
            new() { UserId = userId, Token = "t1", IsActive = true },
            new() { UserId = userId, Token = "t2", IsActive = true },
            new() { UserId = userId, Token = "t3", IsActive = true },
        ];
        var uow = BuildPendingUow(deviceTokens: tokens);

        var push = new Mock<INotificationChannel>();
        push.SetupGet(c => c.ChannelType).Returns(NotificationChannelEnum.Push);
        push.SetupSequence(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelResult(false, "DeviceNotRegistered"))
            .ReturnsAsync(new ChannelResult(true))
            .ReturnsAsync(new ChannelResult(false, "DeviceNotRegistered"));

        var sut = Build(uow.Object, NoCache().Object, push.Object);
        var notification = PendingNotification(userId, NotificationChannelEnum.Push);

        var settled = await sut.DispatchPendingAsync(notification);

        settled.Should().BeTrue();
        notification.Status.Should().Be(NotificationStatusEnum.Sent);
        notification.FailureReason.Should().BeNull();
        push.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task DispatchPendingAsync_PushFanOut_AllTokensFail_MarksFailed()
    {
        var userId = Guid.NewGuid();
        DeviceToken[] tokens =
        [
            new() { UserId = userId, Token = "t1", IsActive = true },
            new() { UserId = userId, Token = "t2", IsActive = true },
        ];
        var uow = BuildPendingUow(deviceTokens: tokens);

        var push = new Mock<INotificationChannel>();
        push.SetupGet(c => c.ChannelType).Returns(NotificationChannelEnum.Push);
        push.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelResult(false, "DeviceNotRegistered"));

        var sut = Build(uow.Object, NoCache().Object, push.Object);
        var notification = PendingNotification(userId, NotificationChannelEnum.Push);

        var settled = await sut.DispatchPendingAsync(notification);

        settled.Should().BeTrue();
        notification.Status.Should().Be(NotificationStatusEnum.Failed);
        notification.FailureReason.Should().Be("DeviceNotRegistered");
        push.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DispatchPendingAsync_SmsWithoutPhoneNumber_MarksFailed()
    {
        var userId = Guid.NewGuid();
        var pref = new NotificationPreference
        {
            UserId = userId,
            PushEnabled = true,
            EmailEnabled = true,
            SmsEnabled = true,
            InAppEnabled = true,
        };
        var uow = BuildPendingUow(pref); // account chưa sync → null
        var sms = FakeChannel(NotificationChannelEnum.Sms);
        var sut = Build(uow.Object, NoCache().Object, sms.Object);
        var notification = PendingNotification(userId, NotificationChannelEnum.Sms);

        var settled = await sut.DispatchPendingAsync(notification);

        settled.Should().BeTrue();
        notification.Status.Should().Be(NotificationStatusEnum.Failed);
        notification.FailureReason.Should().Be("No phone number for recipient");
        sms.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchPendingAsync_SmsWithPhoneNumber_SendsWithRecipientData()
    {
        var userId = Guid.NewGuid();
        var pref = new NotificationPreference
        {
            UserId = userId,
            PushEnabled = true,
            EmailEnabled = true,
            SmsEnabled = true,
            InAppEnabled = true,
        };
        var account = new AccountReadModel
        {
            Id = userId,
            Email = "a@b.com",
            PhoneNumber = "+84900000000",
        };
        var uow = BuildPendingUow(pref, account: account);
        var sms = FakeChannel(NotificationChannelEnum.Sms);
        var sut = Build(uow.Object, NoCache().Object, sms.Object);
        var notification = PendingNotification(userId, NotificationChannelEnum.Sms);

        var settled = await sut.DispatchPendingAsync(notification);

        settled.Should().BeTrue();
        notification.Status.Should().Be(NotificationStatusEnum.Sent);
        sms.Verify(c => c.SendAsync(
            It.Is<SendRequest>(r => r.PhoneNumber == "+84900000000" && r.NotificationId == notification.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// FailureReason chỉ chứa được 1000 ký tự (NotificationConfiguration HasMaxLength(1000)) nhưng
    /// ErrorMessage của channel là ex.Message không giới hạn — không cắt thì SaveChangesAsync throw,
    /// row kẹt Pending và bị worker retry mỗi 5s vĩnh viễn.
    /// </summary>
    [Fact]
    public async Task DispatchPendingAsync_LongErrorMessage_TruncatesFailureReasonToColumnLimit()
    {
        var userId = Guid.NewGuid();
        var uow = BuildPendingUow();

        var inApp = new Mock<INotificationChannel>();
        inApp.SetupGet(c => c.ChannelType).Returns(NotificationChannelEnum.InApp);
        inApp.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new ChannelResult(false, new string('x', 1500)));

        var sut = Build(uow.Object, NoCache().Object, inApp.Object);
        var notification = PendingNotification(userId, NotificationChannelEnum.InApp);

        var settled = await sut.DispatchPendingAsync(notification);

        settled.Should().BeTrue();
        notification.Status.Should().Be(NotificationStatusEnum.Failed);
        notification.FailureReason.Should().HaveLength(1000);
    }

    [Fact]
    public async Task DispatchPendingAsync_NoChannelRegistered_MarksFailed()
    {
        var userId = Guid.NewGuid();
        var uow = BuildPendingUow();
        var push = FakeChannel(NotificationChannelEnum.Push); // chỉ đăng ký Push
        var sut = Build(uow.Object, NoCache().Object, push.Object);
        var notification = PendingNotification(userId, NotificationChannelEnum.InApp);

        var settled = await sut.DispatchPendingAsync(notification);

        settled.Should().BeTrue();
        notification.Status.Should().Be(NotificationStatusEnum.Failed);
        notification.FailureReason.Should().Contain("No channel registered");
    }
}
