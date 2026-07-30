using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Domain.Enums;

namespace NotificationService.UnitTests.Handlers.Notification;

public class CreateNotificationCommandValidationTests
{
    private static CreateNotificationCommand Valid(Guid userId) => new()
    {
        UserId = userId,
        Type = NotificationTypeEnum.TicketCreated,
        Channel = NotificationChannelEnum.InApp,
        Title = "Tiêu đề",
        Body = "Nội dung",
    };

    /// <summary>
    /// **Đảo ngược 30/07/2026.** Test này trước đây khẳng định <c>Guid.Empty</c> ĐƯỢC chấp nhận,
    /// theo ghi chú GH-594 ("broadcast placeholder, dispatcher resolve recipient sau").
    ///
    /// Thiết kế đó không tồn tại trong code: đã rà đủ 9 consumer dùng command này — tất cả resolve
    /// recipient thật trước khi gửi; dispatcher (Sprint 6.2) không resolve broadcast mà đánh Failed
    /// ngay với lý do <c>empty_user_id</c>. Nói cách khác bản ghi UserId rỗng là bản ghi chắc chắn
    /// hỏng, và việc "chấp nhận" nó chỉ tạo ra rác.
    ///
    /// Chi tiết: xem <c>CreateNotificationUserIdTests</c>.
    /// </summary>
    [Fact]
    public async Task EmptyUserId_IsRejected()
    {
        var cmd = Valid(Guid.Empty);

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(400);
        resp.ListErrors.Should().Contain(e => e.Field == "UserId");
    }

    [Fact]
    public async Task RealUserId_PassesValidation()
    {
        var resp = await Valid(Guid.NewGuid()).ValidateAsync();

        resp.IsSuccess.Should().BeTrue();
        resp.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingTitle_StillFails()
    {
        var cmd = Valid(Guid.Empty);
        cmd.Title = "";

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(400);
        resp.ListErrors.Should().Contain(e => e.Field == "Title");
    }

    [Fact]
    public async Task InvalidType_StillFails()
    {
        var cmd = Valid(Guid.Empty);
        cmd.Type = (NotificationTypeEnum)9999;

        var resp = await cmd.ValidateAsync();

        resp.IsSuccess.Should().BeFalse();
        resp.ListErrors.Should().Contain(e => e.Field == "Type");
    }
}
