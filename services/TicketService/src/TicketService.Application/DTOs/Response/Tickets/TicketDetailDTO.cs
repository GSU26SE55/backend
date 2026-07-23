using TicketService.Application.DTOs.Response.Maintenances;
using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Tickets;

public class TicketDetailDTO : TicketDTO
{
    /// <summary>
    /// Mô tả chi tiết.
    /// </summary>
    public string Description { get; set; } = string.Empty;
    public string? ResolutionSummary { get; set; }
    public DateTime? ResolvedAt { get; set; }
    /// <summary>
    /// Resolved by staff id.
    /// </summary>
    public string? ResolvedByStaffId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedByManagerId { get; set; }
    /// <summary>
    /// Rejection reason.
    /// </summary>
    public string? RejectionReason { get; set; }
    public DateTime? ClosedAt { get; set; }
    public short? Rating { get; set; }
    /// <summary>
    /// Rating comment.
    /// </summary>
    public string? RatingComment { get; set; }
    public DateTime? RatedAt { get; set; }
    public DateTime? EscalatedAt { get; set; }
    /// <summary>
    /// Escalation reason.
    /// </summary>
    public EscalationReasonEnum? EscalationReason { get; set; }
    /// <summary>
    /// Thời điểm bắt đầu phát hiện sự cố (do customer khai báo khi tạo ticket).
    /// </summary>
    public DateTime? IncidentDetectedFrom { get; set; }
    /// <summary>
    /// Thời điểm kết thúc phát hiện sự cố. Null nếu ticket được tạo tự động từ alert.
    /// </summary>
    public DateTime? IncidentDetectedTo { get; set; }
    public string? OriginAlertId { get; set; }
    public List<TicketActivityDTO> Activities { get; set; } = new();
    /// <summary>
    /// Chats.
    /// </summary>
    public List<TicketChatDTO> Chats { get; set; } = new();
    public List<MaintenanceLogDTO> MaintenanceLogs { get; set; } = new();
    public List<string> AttachmentFileIds { get; set; } = new();
}
