using System.ComponentModel.DataAnnotations.Schema;
using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class Ticket : AuditableEntity
{
    public required string Code { get; set; }
    public Guid BatteryAssetId { get; set; }
    public Guid CustomerId { get; set; }

    /// <summary>
    /// In-memory only — not persisted. Populated by handlers before calling the state machine.
    /// Replaces the removed AssignedStaffId column; state machine checks this instead.
    /// </summary>
    [NotMapped]
    public Guid? PrimaryHandlerStaffId { get; set; }
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

    /// <summary>
    /// Site nơi ticket phát sinh. Ticket site-level (environmental) lấy thẳng từ event; ticket pin
    /// lấy site của battery asset lúc tạo.
    ///
    /// Trước đây ticket chỉ có BatteryAssetId + CustomerId, nên "các ticket cùng một site" không
    /// truy vấn được nếu không hỏi vòng sang BatteryService — mà ticket environmental thì
    /// BatteryAssetId = Guid.Empty, tức không có đường nào lần ra site. Lưu thẳng ở đây để
    /// gom ticket theo site (sự cố môi trường + các ticket pin cùng cabinet) bằng 1 query.
    ///
    /// Null với ticket cũ tạo trước migration này, và với ticket không xác định được site.
    /// </summary>
    public Guid? SiteId { get; set; }

    /// <summary>
    /// Loại bất thường đã sinh ra ticket (<c>AnomalyTypeEnum</c> của BatteryService).
    /// <c>null</c> với ticket không đến từ alert (khách tự tạo, bảo trì định kỳ).
    /// </summary>
    /// <remarks>
    /// Đây là ĐỊNH DANH của sự cố, khác với <see cref="Category"/> — category chỉ nói "cần thợ
    /// tới sửa" nên cả năm loại môi trường đều là <c>Repair</c>.
    ///
    /// <para>Thiếu cột này thì không có gì phân biệt được ba sự cố môi trường ở cùng một site:
    /// chúng dùng chung <c>BatteryAssetId = Guid.Empty</c> và chung category, nên ràng buộc
    /// unique gom cả ba vào MỘT ticket — gas nổ trước thì nước và nhiệt độ sau đó chỉ được gắn
    /// vào ticket gas, dù là ba sự cố khác nhau cần ba cách xử lý khác nhau.</para>
    /// </remarks>
    public int? AnomalyType { get; set; }

    public int ReopenCount { get; set; }
    public string? ResolutionSummary { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedByStaffId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public Guid? ApprovedByManagerId { get; set; }
    public string? Reason { get; set; }
    public DateTime? ClosedAt { get; set; }
    public TicketCloseReasonEnum? CloseReason { get; set; }
    public short? Rating { get; set; }
    public string? RatingComment { get; set; }
    public DateTime? RatedAt { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public EscalationReasonEnum? EscalationReason { get; set; }
    public bool IsIncident { get; set; }
    public DateTime? ScheduledStartAtUtc { get; set; }
    public int ScheduleVersion { get; set; }
    public DateTime? PeriodicMaintenanceDueAtUtc { get; set; }
    public DateTime? PeriodicMaintenanceScheduleDeadlineAtUtc { get; set; }
    public DateTime? PeriodicMaintenanceReminder1SentAtUtc { get; set; }
    public DateTime? PeriodicMaintenanceReminder2SentAtUtc { get; set; }
    public DateTime? PeriodicMaintenanceManagerEscalatedAtUtc { get; set; }
    public DateTime? PeriodicMaintenanceCustomerScheduledAtUtc { get; set; }
    public PendingContextEnum? PendingContext { get; set; }
    public PauseReasonEnum? PendingReason { get; set; }
    public Guid? ActiveIncidentEpisodeId { get; set; }

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

    // ── Quan hệ cha–con (KHÁC merge) ──
    /// <summary>
    /// Ticket cha — dùng khi nhiều ticket cùng MỘT nguyên nhân gốc: sự cố môi trường ở cabinet
    /// (ticket cha) kéo theo các ticket pin do Customer báo (ticket con).
    ///
    /// KHÁC <see cref="MergedIntoTicketId"/> một cách có chủ đích. Merge nghĩa là "trùng lặp":
    /// nó ĐÓNG ticket nguồn với CloseReason = MergedDuplicate và DỪNG SLA timer. Với tình huống
    /// này thì sai — 4 cục pin vẫn phải được kiểm tra sau khi dập xong sự cố môi trường, nên
    /// ticket con phải SỐNG và giữ SLA riêng. Link chỉ nói "cùng nguyên nhân", không nói
    /// "cùng khối lượng công việc".
    ///
    /// Vì vậy đóng ticket cha KHÔNG đóng ticket con.
    /// </summary>
    public Guid? ParentTicketId { get; set; }

    // Navigation properties
    // EF Core maps this collection; callers use the two [NotMapped] props below.
    public ICollection<SlaTimer> SlaTimers { get; set; } = new List<SlaTimer>();

    [NotMapped]
    public SlaTimer? ResponseSlaTimer
        => SlaTimers.FirstOrDefault(t => t.Type == SlaTimerTypeEnum.Response);

    [NotMapped]
    public SlaTimer? ResolutionSlaTimer
        => SlaTimers.FirstOrDefault(t => t.Type == SlaTimerTypeEnum.Resolution);
    public ICollection<TicketActivity> Activities { get; set; } = new List<TicketActivity>();
    public ICollection<TicketChat> Chats { get; set; } = new List<TicketChat>();
    public ICollection<MaintenanceLog> MaintenanceLogs { get; set; } = new List<MaintenanceLog>();
    public ICollection<TicketAttachment> Attachments { get; set; } = new List<TicketAttachment>();
    public ICollection<TicketKbReference> KbReferences { get; set; } = new List<TicketKbReference>();
    public ICollection<TicketParticipant> Participants { get; set; } = new List<TicketParticipant>();
    public ICollection<TicketBatteryAsset> BatteryAssets { get; set; } = new List<TicketBatteryAsset>();
    public ICollection<TicketAssignment> Assignments { get; set; } = new List<TicketAssignment>();
}
