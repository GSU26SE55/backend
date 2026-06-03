using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Ticket;

public class TicketActivityDTO
{
    public string Id { get; set; } = string.Empty;
    public string TicketId { get; set; } = string.Empty;
    public string? ActorUserId { get; set; }
    public ActorRoleEnum ActorRole { get; set; }
    public string? ActorDisplayName { get; set; }
    public ActivityActionEnum Action { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}
