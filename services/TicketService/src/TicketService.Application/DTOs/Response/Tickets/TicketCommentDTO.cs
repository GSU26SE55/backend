using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Tickets;

public class TicketCommentDTO
{
    public string Id { get; set; } = string.Empty;
    public string TicketId { get; set; } = string.Empty;
    public string AuthorUserId { get; set; } = string.Empty;
    public ActorRoleEnum AuthorRole { get; set; }
    public string? AuthorDisplayName { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsInternal { get; set; }
    public List<string> AttachmentFileIds { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}
