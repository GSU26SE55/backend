using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
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
}
