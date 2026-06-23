using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Tickets;

public class ParticipantHistoryDTO
{
    public string Id { get; set; } = string.Empty;
    public string TicketId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public ActorRoleEnum UserRole { get; set; }
    public ParticipantTypeEnum ParticipantType { get; set; }
    public bool CanPost { get; set; }
    public bool CanViewInternal { get; set; }
    public string AddedByUserId { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
    public DateTime? RemovedAt { get; set; }
    public string? RemovedByUserId { get; set; }
    public string? RemoveReason { get; set; }
}
