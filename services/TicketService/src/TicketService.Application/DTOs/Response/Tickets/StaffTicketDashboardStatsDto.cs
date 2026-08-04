namespace TicketService.Application.DTOs.Response.Tickets;

/// <summary>
/// Snapshot KPI ticket cho Dashboard + SLA Monitor của chính Staff đang đăng nhập
/// (scope theo AssignedStaffId lấy từ JWT — không nhận tham số).
/// </summary>
public class StaffTicketDashboardStatsDto
{
    /// <summary>Số ticket đang phụ trách (status không thuộc nhóm Resolved/ClosedPendingRate/Closed/ClosedRejected).</summary>
    public int OpenCount { get; set; }

    /// <summary>Số ticket đã xử lý (Resolved/ClosedPendingRate/Closed — không gồm ClosedRejected).</summary>
    public int ResolvedCount { get; set; }

    /// <summary>Số ticket đang theo dõi SLA có timer Running còn ≤ 25% thời gian.</summary>
    public int NearBreachCount { get; set; }

    /// <summary>Số ticket đang theo dõi SLA có timer đã Breached.</summary>
    public int BreachedCount { get; set; }

    /// <summary>Số ticket đang theo dõi SLA có timer đang Paused.</summary>
    public int PausedCount { get; set; }

    /// <summary>Tổng ticket đang theo dõi SLA — status ∈ Assigned/InProgress/WaitingCustomer/WaitingParts/WaitingOnsiteSchedule/Escalated và có SLA timer.</summary>
    public int SlaMonitoredCount { get; set; }

    /// <summary>Tổng hợp SLA timer của toàn bộ ticket được gán cho staff này.</summary>
    public SlaSummaryDto Sla { get; set; } = new();

    /// <summary>Số ticket theo từng status — đủ 14 status, status không có ticket = 0.</summary>
    public Dictionary<string, int> CountByStatus { get; set; } = new();

    /// <summary>Phân bố rủi ro SLA trên nhóm đang theo dõi. Healthy = SlaMonitoredCount − Near − Breached (gồm cả Paused).</summary>
    public SlaRiskDto SlaRisk { get; set; } = new();

    /// <summary>Số ticket được gán cho staff tạo mới theo ngày (bucket UTC) — 7 ngày gần nhất, ngày trống = 0.</summary>
    public List<DailyCountPointDto> CreatedTrend7Days { get; set; } = new();
}

/// <summary>Phân bố rủi ro SLA trên nhóm ticket đang theo dõi.</summary>
public class SlaRiskDto
{
    /// <summary>Timer còn &gt; 25% thời gian hoặc đang Paused.</summary>
    public int Healthy { get; set; }

    /// <summary>Timer Running còn ≤ 25% thời gian.</summary>
    public int Near { get; set; }

    /// <summary>Timer đã Breached.</summary>
    public int Breached { get; set; }
}
