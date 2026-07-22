using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class Ticket : AuditableEntity
{
    public required string Code { get; set; }
    public Guid BatteryAssetId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? AssignedStaffId { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public TicketCategoryEnum Category { get; set; }
    public TicketPriorityEnum? Priority { get; set; }
    public ImpactScopeEnum? ImpactScope { get; set; }
    public UrgencyLevelEnum? UrgencyLevel { get; set; }
    public TicketStatusEnum Status { get; set; }
    public TicketOriginEnum Origin { get; set; }
    public Guid? OriginAlertId { get; set; } //link tới Alert  nếu auto

    /// <summary>
    /// Sprint Bonus NS-22 (#662, E2) — link tới EnvironmentalIncident (site-level) khi ticket được
    /// auto-tạo từ sự cố môi trường (khói/ngập). Null nếu ticket không phải từ environmental incident.
    /// </summary>
    public Guid? EnvironmentalIncidentId { get; set; }

    public int ReopenCount { get; set; }
    public string? ResolutionSummary { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedByStaffId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public Guid? ApprovedByManagerId { get; set; }
    public string? Reason { get; set; }
    public DateTime? ClosedAt { get; set; }
    public short? Rating { get; set; }
    public string? RatingComment { get; set; }
    public DateTime? RatedAt { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public EscalationReasonEnum? EscalationReason { get; set; }
    public bool IsIncident { get; set; }

    /// <summary>
    /// Thời điểm Customer phát hiện pin bất thường (Customer điền khi tạo ticket thủ công).
    /// Khác CreatedAt (thời điểm tạo ticket). Dùng để AI đối chiếu sensor tại thời điểm đó.
    /// </summary>
    public DateTime? DetectedAt { get; set; }

    /// <summary>
    /// Snapshot serial number của pin lúc tạo ticket (denormalize từ BatteryService) —
    /// để FE hiển thị tên pin không cần gọi thêm API. Null nếu lookup fail (không chặn tạo ticket).
    /// </summary>
    public string? BatterySerialNumber { get; set; }

    // ── AI verify (chấm điểm thật/rác — human-in-the-loop, KHÔNG tự chặn) ──
    /// <summary>Trạng thái AI verify: Pending → Legitimate/Suspicious/Skipped.</summary>
    public TicketVerifyStatusEnum AiVerifyStatus { get; set; } = TicketVerifyStatusEnum.Pending;
    /// <summary>Điểm hợp lệ [0..1] từ AI (1=chắc chắn thật). Null khi chưa verify/skip.</summary>
    public double? AiVerifyScore { get; set; }
    /// <summary>Lý do AI đưa ra verdict (để Manager tham khảo).</summary>
    public string? AiVerifyReason { get; set; }

    // ── Merge / duplicate (cờ nghi trùng + Manager quyết gộp) ──
    /// <summary>Ticket bị nghi trùng với ticket này (cùng pin/chủ, còn mở, AI so mô tả). Null nếu không nghi.</summary>
    public Guid? SuspectedDuplicateOfTicketId { get; set; }
    /// <summary>Lý do nghi trùng (cùng pin + category + độ tương đồng mô tả).</summary>
    public string? DuplicateReason { get; set; }
    /// <summary>Set khi Manager gộp ticket NÀY vào ticket khác — ticket này coi như đã gộp (ẩn khỏi queue).</summary>
    public Guid? MergedIntoTicketId { get; set; }

    // Navigation properties
    public SlaTimer? SlaTimer { get; set; }
    public ICollection<TicketActivity> Activities { get; set; } = new List<TicketActivity>();
    public ICollection<TicketChat> Chats { get; set; } = new List<TicketChat>();
    public ICollection<MaintenanceLog> MaintenanceLogs { get; set; } = new List<MaintenanceLog>();
    public ICollection<TicketAttachment> Attachments { get; set; } = new List<TicketAttachment>();
    public ICollection<TicketKbReference> KbReferences { get; set; } = new List<TicketKbReference>();
    public ICollection<TicketParticipant> Participants { get; set; } = new List<TicketParticipant>();
    public ICollection<TicketBatteryAsset> BatteryAssets { get; set; } = new List<TicketBatteryAsset>();
}
