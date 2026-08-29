using MockQueryable.Moq;
using NotificationService.Application.CQRS.Command.Preference;
using NotificationService.Application.CQRS.Handler.Preference;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Entities;
using SharedContracts.Interfaces;
using SharedKernels.Interfaces;

namespace NotificationService.UnitTests.Handlers.Preference;

/// <summary>
/// Màn Profile của FE chỉ PUT 4 kênh + quiet hours + timezone. Khi các pref chat là
/// non-nullable, key vắng mặt bind về default C# rồi handler ghi đè thẳng — user bật
/// NotifyOnReaction ở chỗ khác, sau đó chỉ bật SMS trong Profile là mất luôn lựa chọn cũ,
/// không báo lỗi gì. Các pref chat giờ nullable: null = giữ nguyên.
/// </summary>
public class UpdatePreferencePartialTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static (UpdateNotificationPreferenceCommandHandler Handler, NotificationPreference Existing)
        Build()
    {
        var existing = new NotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            PushEnabled = true,
            EmailEnabled = true,
            SmsEnabled = false,
            InAppEnabled = true,
            TimeZone = "Asia/Ho_Chi_Minh",
            // Người dùng đã chỉnh các giá trị này ở nơi khác, khác hẳn default.
            NotifyOnChat = false,
            NotifyOnMention = false,
            NotifyOnReaction = true,
            DigestWindowMinutes = 30,
        };

        var repo = new Mock<IGenericRepository<NotificationPreference>>();
        repo.Setup(r => r.GetAllAsync())
            .Returns(new[] { existing }.AsQueryable().BuildMock());

        var uow = new Mock<INotificationUnitOfWork>();
        uow.SetupGet(u => u.NotificationPreferences).Returns(repo.Object);

        var handler = new UpdateNotificationPreferenceCommandHandler(
            uow.Object, new Mock<ICacheService>().Object);

        return (handler, existing);
    }

    /// <summary>PUT chỉ có 4 kênh (đúng những gì màn Profile gửi) không được đụng vào pref chat.</summary>
    [Fact]
    public async Task ChannelOnlyUpdate_KeepsChatPreferences()
    {
        var (handler, existing) = Build();

        await handler.Handle(new UpdateNotificationPreferenceCommand
        {
            UserId = UserId,
            PushEnabled = true,
            EmailEnabled = true,
            SmsEnabled = true,   // người dùng chỉ bật SMS
            InAppEnabled = true,
            TimeZone = "Asia/Ho_Chi_Minh",
        }, CancellationToken.None);

        existing.SmsEnabled.Should().BeTrue("đây là thứ người dùng thực sự đổi");

        existing.NotifyOnChat.Should().BeFalse("không gửi field thì phải giữ nguyên");
        existing.NotifyOnMention.Should().BeFalse();
        existing.NotifyOnReaction.Should().BeTrue();
        existing.DigestWindowMinutes.Should().Be(30);
    }

    /// <summary>Có gửi thì vẫn phải ghi đè — nullable không được biến field thành read-only.</summary>
    [Fact]
    public async Task ExplicitChatPreferences_AreApplied()
    {
        var (handler, existing) = Build();

        await handler.Handle(new UpdateNotificationPreferenceCommand
        {
            UserId = UserId,
            PushEnabled = true,
            EmailEnabled = true,
            SmsEnabled = false,
            InAppEnabled = true,
            TimeZone = "Asia/Ho_Chi_Minh",
            NotifyOnChat = true,
            NotifyOnMention = true,
            NotifyOnReaction = false,
            DigestWindowMinutes = 15,
        }, CancellationToken.None);

        existing.NotifyOnChat.Should().BeTrue();
        existing.NotifyOnMention.Should().BeTrue();
        existing.NotifyOnReaction.Should().BeFalse();
        existing.DigestWindowMinutes.Should().Be(15);
    }
}
