using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Tickets;

public class ParticipantHistoryDTO
{
    /// <summary>
    /// Id.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    public string TicketId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    /// <summary>
    /// Vai trò của người thực hiện.
    /// </summary>
    public ActorRoleEnum UserRole { get; set; }
    public ParticipantTypeEnum ParticipantType { get; set; }
    public bool CanPost { get; set; }
    /// <summary>
    /// Can view internal.
    /// </summary>
    public bool CanViewInternal { get; set; }
    public string AddedByUserId { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
    /// <summary>
    /// Removed at.
    /// </summary>
    public DateTime? RemovedAt { get; set; }
    public string? RemovedByUserId { get; set; }
    public string? RemoveReason { get; set; }
}
