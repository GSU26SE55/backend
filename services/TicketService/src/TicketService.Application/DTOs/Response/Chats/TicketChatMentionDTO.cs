using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Chats;

public class TicketChatMentionDTO
{
    /// <summary>
    /// Id.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
    public string? TicketId { get; set; }
    /// <summary>
    /// Mentioned user id.
    /// </summary>
    public string MentionedUserId { get; set; } = string.Empty;
    public ActorRoleEnum MentionedUserRole { get; set; }
    public string? MentionedDisplayName { get; set; }
    public bool IsInternal { get; set; }
    public DateTime CreatedAt { get; set; }
}
