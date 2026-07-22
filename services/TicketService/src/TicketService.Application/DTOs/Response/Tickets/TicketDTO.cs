using TicketService.Application.DTOs.Response.SLA;
using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Tickets;

public class TicketDTO
{
    /// <summary>
    /// Id.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string BatteryAssetId { get; set; } = string.Empty;
    public List<string> BatteryAssetIds { get; set; } = new();
    /// <summary>
    /// Customer id.
    /// </summary>
    public string CustomerId { get; set; } = string.Empty;
    public string? AssignedStaffId { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>
    /// Danh mục phân loại.
    /// </summary>
    public TicketCategoryEnum Category { get; set; }
    public TicketPriorityEnum? Priority { get; set; }
    public ImpactScopeEnum? ImpactScope { get; set; }
    /// <summary>
    /// Urgency level.
    /// </summary>
    public UrgencyLevelEnum? UrgencyLevel { get; set; }
    public TicketStatusEnum Status { get; set; }
    public TicketOriginEnum Origin { get; set; }
    /// <summary>
    /// Reopen count.
    /// </summary>
    public int ReopenCount { get; set; }
    public bool IsIncident { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>
    /// Thời gian cập nhật (UTC).
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
    public SlaTimerDTO? SlaTimer { get; set; }
    public bool HasUnreadChat { get; set; }

    // ── Verify + merge (GH-ticket-verify) ──
    /// <summary>Thời điểm Customer phát hiện (nếu điền khi tạo).</summary>
    public DateTime? DetectedAt { get; set; }
    /// <summary>Serial pin snapshot lúc tạo ticket (null nếu lookup fail).</summary>
    public string? BatterySerialNumber { get; set; }
    /// <summary>Trạng thái AI verify: Pending/Legitimate/Suspicious/Skipped.</summary>
    public TicketVerifyStatusEnum AiVerifyStatus { get; set; }
    /// <summary>Điểm hợp lệ [0..1] từ AI.</summary>
    public double? AiVerifyScore { get; set; }
    /// <summary>Lý do AI verdict.</summary>
    public string? AiVerifyReason { get; set; }
    /// <summary>Ticket bị nghi trùng với ticket này (null nếu không nghi).</summary>
    public string? SuspectedDuplicateOfTicketId { get; set; }
    /// <summary>Lý do nghi trùng.</summary>
    public string? DuplicateReason { get; set; }
    /// <summary>Ticket đích nếu ticket này đã bị gộp (null nếu chưa gộp).</summary>
    public string? MergedIntoTicketId { get; set; }
}
