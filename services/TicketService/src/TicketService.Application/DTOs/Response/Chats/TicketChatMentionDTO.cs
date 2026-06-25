using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Chats;

public class TicketChatMentionDTO
{
    public string Id { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
    public string? TicketId { get; set; }
    public string MentionedUserId { get; set; } = string.Empty;
    public ActorRoleEnum MentionedUserRole { get; set; }
    public string? MentionedDisplayName { get; set; }
    public bool IsAcknowledged { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
