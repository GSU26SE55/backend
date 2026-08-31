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
    public List<TicketAssignmentDTO> Assignments { get; set; } = new();
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

    /// <summary>
    /// Sự cố môi trường đã sinh ra ticket này (khói, rò khí, ngập). <c>null</c> với mọi ticket khác.
    ///
    /// <para>Ticket cấp site có <c>BatteryAssetId = Guid.Empty</c> vì sự cố nằm ở tủ điện chứ không
    /// ở một viên pin. Thiếu trường này, FE không phân biệt được "ticket không gắn pin" với "ticket
    /// gắn pin nhưng dữ liệu chưa về", nên nó dựng khuôn của ticket pin rồi hiển thị một loạt ô
    /// trống — kể cả dòng "Battery serial —" vốn không bao giờ có giá trị. Bằng chứng thật (số đo
    /// cảm biến MQ-2) thì bị nhét trong câu mô tả tự sinh, không tra cứu được.</para>
    /// </summary>
    public string? EnvironmentalIncidentId { get; set; }

    public DateTime? ScheduledStartAtUtc { get; set; }
    public int ScheduleVersion { get; set; }
    public DateTime? PeriodicMaintenanceDueAtUtc { get; set; }
    public DateTime? PeriodicMaintenanceScheduleDeadlineAtUtc { get; set; }

    /// <summary>
    /// Ticket này thuộc một kỳ bảo trì định kỳ của pin.
    /// </summary>
    /// <remarks>
    /// Nhận diện bằng hạn kỳ, không bằng ticket nguồn. Từ khi lịch bảo trì chuyển sang tầng
    /// tài sản, ticket sinh ra từ một kỳ của pin chứ không neo vào ticket đã đóng, nên
    /// <c>PeriodicMaintenanceSourceTicketId</c> luôn trống — dùng nó thì cờ này vĩnh viễn
    /// false và huy hiệu "bảo trì định kỳ" trên web không bao giờ hiện.
    /// </remarks>
    public bool IsPeriodicMaintenance => PeriodicMaintenanceDueAtUtc is not null;
    public bool IsPeriodicMaintenanceOverdue =>
        PeriodicMaintenanceDueAtUtc.HasValue && PeriodicMaintenanceDueAtUtc.Value < DateTime.UtcNow;
    public PendingContextEnum? PendingContext { get; set; }
    public PauseReasonEnum? PendingReason { get; set; }
    public string? ActiveIncidentEpisodeId { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>
    /// Thời gian cập nhật (UTC).
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
    public SlaTimerDTO? SlaTimer { get; set; }

    /// <summary>
    /// Ngày dự kiến hoàn thành (UTC) — lấy từ <c>SlaTimer.DueAt</c>. Đây là field duy nhất
    /// Customer thấy về SLA; FE format DATE-ONLY. Null khi ticket chưa có SLA timer.
    /// </summary>
    public DateTime? ExpectedCompletionAtUtc { get; set; }
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

    /// <summary>Site của ticket — null nếu chưa xác định được (ticket cũ, hoặc auto-from-alert).</summary>
    public string? SiteId { get; set; }

    /// <summary>
    /// Ticket cha (cùng nguyên nhân gốc). KHÁC MergedIntoTicketId: link không đóng ticket này,
    /// SLA vẫn chạy. Xem Ticket.ParentTicketId.
    /// </summary>
    public string? ParentTicketId { get; set; }
    public TicketCloseReasonEnum? CloseReason { get; set; }
}
