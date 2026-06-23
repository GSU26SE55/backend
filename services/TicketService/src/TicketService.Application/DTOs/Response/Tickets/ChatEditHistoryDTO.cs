using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Tickets;

public class ChatEditHistoryDTO
{
    public string Id { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
    public string OldBody { get; set; } = string.Empty;
    public string NewBody { get; set; } = string.Empty;
    public DateTime EditedAt { get; set; }
    public string EditedByUserId { get; set; } = string.Empty;
    public ActorRoleEnum EditedByRole { get; set; }
    public string? EditReason { get; set; }
}
