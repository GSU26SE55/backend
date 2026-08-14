using System.Text.Json.Serialization;
using MediatR;
using TicketService.Application.DTOs.Response.Chats;

namespace TicketService.Application.CQRS.Command.ChatAi;

public class ChatTranslateCommand : IRequest<ChatTranslateResponse>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; set; }

    /// <summary>
    /// ID của Chat/Bình luận.
    /// </summary>
    [JsonIgnore]
    public Guid ChatId { get; set; }

    /// <summary>
    /// ID của người dùng hiện tại thực hiện hành động.
    /// </summary>
    [JsonIgnore]
    public Guid CurrentUserId { get; set; }

    /// <summary>
    /// Mã ngôn ngữ đích cần dịch sang (ví dụ: "en", "vi").
    /// </summary>
    [JsonIgnore]
    public string TargetLanguage { get; set; } = string.Empty;

    /// <summary>
    /// Roles của người dùng hiện tại — dùng để kiểm tra quyền truy cập ticket và nội bộ chat.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> CurrentUserRoles { get; set; } = [];
}
