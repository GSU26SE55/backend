namespace TicketService.Application.DTOs.Response.Tickets;

/// <summary>
/// Snapshot KPI ticket toàn hệ thống cho Dashboard Admin/Manager.
/// Không nhận tham số thời gian — luôn phản ánh trạng thái hiện tại (trend cố định 7 ngày gần nhất theo UTC).
/// </summary>
public class TicketDashboardStatsDto
{
    /// <summary>Tổng số ticket (không tính đã xóa).</summary>
    public int Total { get; set; }

    /// <summary>Số ticket đang mở — Open/Pending/InProgress/Request/ReAssign.</summary>
    public int OpenCount { get; set; }

    /// <summary>Tổng hợp SLA timer toàn hệ thống theo trạng thái.</summary>
    public SlaSummaryDto Sla { get; set; } = new();

    /// <summary>Số ticket theo từng status — đủ 14 status, status không có ticket = 0. FE tự nhóm pipeline (không gộp ClosedRejected vào "Hoàn tất").</summary>
    public Dictionary<string, int> CountByStatus { get; set; } = new();

    /// <summary>Số ticket theo priority (P1Critical/P2High/P3Normal). Ticket chưa triage (Priority null) không được tính.</summary>
    public Dictionary<string, int> CountByPriority { get; set; } = new();

    /// <summary>Số ticket tạo mới theo ngày (bucket theo UTC) — 7 ngày gần nhất, kể cả hôm nay, ngày trống = 0.</summary>
    public List<DailyCountPointDto> CreatedTrend7Days { get; set; } = new();

    /// <summary>Số ticket mở đang gán cho từng staff (sort giảm dần) — phục vụ widget workload của Manager.</summary>
    public List<StaffOpenCountDto> OpenCountByStaff { get; set; } = new();

    /// <summary>Số ticket cần theo dõi do lạm dụng pause SLA.</summary>
    public int SlaAbuseFlaggedCount { get; set; }

    /// <summary>Các ticket có ít nhất 2 lần pause hoặc tổng thời gian pause từ 1.440 phút.</summary>
    public List<SlaAbuseFlaggedTicketDto> SlaAbuseFlaggedTickets { get; set; } = new();
}

/// <summary>Tổng hợp SLA timer theo trạng thái.</summary>
public class SlaSummaryDto
{
    /// <summary>Số timer đã hoàn thành đúng hạn.</summary>
    public int Met { get; set; }

    /// <summary>Số timer đã breach.</summary>
    public int Breached { get; set; }

    /// <summary>Số timer đang chạy.</summary>
    public int Running { get; set; }

    /// <summary>Số timer đang tạm dừng.</summary>
    public int Paused { get; set; }

    /// <summary>Met / (Met + Breached) × 100, làm tròn 2 chữ số. Bằng 100 khi chưa có timer nào kết thúc (Met + Breached = 0).</summary>
    public double CompliancePercent { get; set; }
}

/// <summary>1 điểm trend theo ngày (bucket UTC).</summary>
public class DailyCountPointDto
{
    /// <summary>Ngày UTC.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Số ticket tạo trong ngày.</summary>
    public int Count { get; set; }
}

/// <summary>Số ticket mở đang gán cho 1 staff.</summary>
public class StaffOpenCountDto
{
    /// <summary>Id staff (AssignedStaffId).</summary>
    public string StaffId { get; set; } = string.Empty;

    /// <summary>Số ticket mở đang phụ trách.</summary>
    public int ActiveCount { get; set; }
}

public class SlaAbuseFlaggedTicketDto
{
    public string TicketId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? PrimaryStaffId { get; set; }
    public int PauseEpisodesCount { get; set; }
    public int TotalPausedMinutes { get; set; }
}
