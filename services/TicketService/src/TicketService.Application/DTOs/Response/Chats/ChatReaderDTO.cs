using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Chats;

public class ChatReaderDTO
{
    /// <summary>
    /// ID của Chat/Bình luận.
    /// </summary>
    public string ChatId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    /// <summary>
    /// Tên hiển thị — resolve từ CustomerAccounts/StaffAccounts theo Role.
    /// Fallback về UserId khi không tìm thấy account (đã xoá / khác service).
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Ảnh đại diện — null thì client tự vẽ chữ cái đầu của <see cref="DisplayName"/>.
    /// Đồng bộ từ AuthService qua AccountProfileUpdatedEvent.
    /// </summary>
    public string? AvatarUrl { get; set; }

    public ActorRoleEnum Role { get; set; }
    /// <summary>
    /// Read at.
    /// </summary>
    public DateTime ReadAt { get; set; }
}
