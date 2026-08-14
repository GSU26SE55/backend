using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Channels;
using NotificationService.UnitTests.Helpers;
using NotificationEntity = NotificationService.Domain.Entities.Notification;

namespace NotificationService.UnitTests.Channels;

public class InAppChannelTests
{
    private static SendRequest MakeRequest(Guid notificationId) => new()
    {
        NotificationId = notificationId,
        UserId = Guid.NewGuid(),
        Title = "Test",
        Body = "Body"
    };

    [Fact]
    public async Task SendAsync_NotificationFound_SetsStatusSentAndReturnsSuccess()
    {
        var notificationId = Guid.NewGuid();
        var notification = new NotificationEntity
        {
            Id = notificationId,
            Status = NotificationStatusEnum.Pending,
            Title = "Test",
            Body = "Body"
        };

        var (uow, _, notificationRepo) = MockNotificationUnitOfWork.Build(notificationSeed: [notification]);
        uow.Setup(u => u.Notifications.GetByIdAsync(notificationId)).ReturnsAsync(notification);

        var channel = new InAppChannel(uow.Object, NullLogger<InAppChannel>.Instance);
        var result = await channel.SendAsync(MakeRequest(notificationId));

        result.Success.Should().BeTrue();
        notification.Status.Should().Be(NotificationStatusEnum.Sent);
        notification.SentAt.Should().NotBeNull();
        notificationRepo.Verify(r => r.UpdateAsync(notification), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_AlreadySent_ReturnsSuccessWithoutUpdate()
    {
        var notificationId = Guid.NewGuid();
        var notification = new NotificationEntity
        {
            Id = notificationId,
            Status = NotificationStatusEnum.Sent,
            SentAt = DateTime.UtcNow.AddMinutes(-1)
        };

        var (uow, _, notificationRepo) = MockNotificationUnitOfWork.Build();
        uow.Setup(u => u.Notifications.GetByIdAsync(notificationId)).ReturnsAsync(notification);

        var channel = new InAppChannel(uow.Object, NullLogger<InAppChannel>.Instance);
        var result = await channel.SendAsync(MakeRequest(notificationId));

        result.Success.Should().BeTrue();
        notificationRepo.Verify(r => r.UpdateAsync(It.IsAny<NotificationEntity>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_NotificationNotFound_ReturnsFailure()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        uow.Setup(u => u.Notifications.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((NotificationEntity?)null);

        var channel = new InAppChannel(uow.Object, NullLogger<InAppChannel>.Instance);
        var result = await channel.SendAsync(MakeRequest(Guid.NewGuid()));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Notification not found");
    }

    // ── 03/08/2026: InApp ghi ngược nội dung đã render ────────────────────────────────────────
    //
    // Với Email/Push/SMS, thứ người dùng nhận là gói tin gửi đi; dòng trong DB chỉ là biên bản.
    // Với InApp thì ngược lại — dòng trong DB CHÍNH LÀ thứ người dùng đọc. Trước thay đổi này
    // dispatcher vẫn render template cho InApp rồi vứt kết quả đi, nên 33 template InApp sửa được,
    // xem trước được, nhưng sửa xong không đổi được chữ nào trên màn hình.

    [Fact]
    public async Task SendAsync_GhiNguocNoiDungDaRender_VaoDongNotification()
    {
        var id = Guid.NewGuid();
        var notification = new NotificationEntity
        {
            Id = id,
            Status = NotificationStatusEnum.Pending,
            Title = "Consumer hardcoded title",
            Body = "Consumer hardcoded body",
        };

        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: [notification]);
        uow.Setup(u => u.Notifications.GetByIdAsync(id)).ReturnsAsync(notification);

        var request = MakeRequest(id);
        request.Title = "Rendered template title";
        request.Body = "Rendered template body";

        var result = await new InAppChannel(uow.Object, NullLogger<InAppChannel>.Instance)
            .SendAsync(request);

        result.Success.Should().BeTrue();
        notification.Title.Should().Be("Rendered template title",
            "feed đọc thẳng từ dòng này, không đọc gói tin nào khác");
        notification.Body.Should().Be("Rendered template body");
    }

    [Fact]
    public async Task SendAsync_NoiDungRenderRong_GiuNguyenNoiDungGoc()
    {
        // Không có template khớp thì dispatcher trả về chính Title/Body inline. Dù vậy vẫn phải
        // phòng trường hợp rỗng: ghi đè bằng chuỗi rỗng là xoá trắng thông báo của người dùng.
        var id = Guid.NewGuid();
        var notification = new NotificationEntity
        {
            Id = id,
            Status = NotificationStatusEnum.Pending,
            Title = "Keep original title",
            Body = "Keep original body",
        };

        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: [notification]);
        uow.Setup(u => u.Notifications.GetByIdAsync(id)).ReturnsAsync(notification);

        var request = MakeRequest(id);
        request.Title = "   ";
        request.Body = "";

        await new InAppChannel(uow.Object, NullLogger<InAppChannel>.Instance).SendAsync(request);

        notification.Title.Should().Be("Keep original title");
        notification.Body.Should().Be("Keep original body");
    }

    [Fact]
    public async Task SendAsync_NoiDungDaiHonCot_ThiCatChuKhongLamVoLenh()
    {
        // title_template tối đa 500 và body_template tối đa 4000, trong khi cột title chỉ 200 và
        // body chỉ 2000. Không cắt thì Postgres ném lỗi và dòng đó kẹt retry vĩnh viễn.
        var id = Guid.NewGuid();
        var notification = new NotificationEntity
        {
            Id = id,
            Status = NotificationStatusEnum.Pending,
            Title = "old",
            Body = "old",
        };

        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: [notification]);
        uow.Setup(u => u.Notifications.GetByIdAsync(id)).ReturnsAsync(notification);

        var request = MakeRequest(id);
        request.Title = new string('T', 500);
        request.Body = new string('B', 4000);

        await new InAppChannel(uow.Object, NullLogger<InAppChannel>.Instance).SendAsync(request);

        notification.Title.Should().HaveLength(200);
        notification.Body.Should().HaveLength(2000);
    }

    [Fact]
    public async Task SendAsync_DaSent_KhongGhiDeNoiDung()
    {
        // Chốt idempotent: nếu ghi lại thì mỗi lần chạy lại dispatcher là một lần nội dung bị thay,
        // và người dùng thấy thông báo cũ đổi chữ dưới tay mình.
        var id = Guid.NewGuid();
        var notification = new NotificationEntity
        {
            Id = id,
            Status = NotificationStatusEnum.Sent,
            Title = "Content already sent",
            Body = "Body already sent",
        };

        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: [notification]);
        uow.Setup(u => u.Notifications.GetByIdAsync(id)).ReturnsAsync(notification);

        var request = MakeRequest(id);
        request.Title = "New content";
        request.Body = "New body";

        await new InAppChannel(uow.Object, NullLogger<InAppChannel>.Instance).SendAsync(request);

        notification.Title.Should().Be("Content already sent");
        notification.Body.Should().Be("Body already sent");
    }

    [Fact]
    public void ChannelType_IsInApp()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        var channel = new InAppChannel(uow.Object, NullLogger<InAppChannel>.Instance);
        channel.ChannelType.Should().Be(NotificationChannelEnum.InApp);
    }
}
