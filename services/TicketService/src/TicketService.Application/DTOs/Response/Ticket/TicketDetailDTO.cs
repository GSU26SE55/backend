using TicketService.Application.DTOs.Response.Maintenance;
using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Ticket;

public class TicketDetailDTO : TicketDTO
{
    public string Description { get; set; } = string.Empty;
    public string? ResolutionSummary { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedByStaffId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedByManagerId { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? ClosedAt { get; set; }
    public short? Rating { get; set; }
    public string? RatingComment { get; set; }
    public DateTime? RatedAt { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public EscalationReasonEnum? EscalationReason { get; set; }
    public string? OriginAlertId { get; set; }
    public List<TicketActivityDTO> Activities { get; set; } = new();
    public List<TicketCommentDTO> Comments { get; set; } = new();
    public List<MaintenanceLogDTO> MaintenanceLogs { get; set; } = new();
    public List<string> AttachmentFileIds { get; set; } = new();
}
