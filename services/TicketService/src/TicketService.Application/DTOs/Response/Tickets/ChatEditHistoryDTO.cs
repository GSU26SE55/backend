using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Tickets;

public class ChatEditHistoryDTO
{
    /// <summary>
    /// Id.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
    public string OldBody { get; set; } = string.Empty;
    /// <summary>
    /// New body.
    /// </summary>
    public string NewBody { get; set; } = string.Empty;
    public DateTime EditedAt { get; set; }
    public string EditedByUserId { get; set; } = string.Empty;
    /// <summary>
    /// Edited by role.
    /// </summary>
    public ActorRoleEnum EditedByRole { get; set; }
    public string? EditReason { get; set; }
}
