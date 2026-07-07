namespace BatteryService.Application.DTOs;

/// <summary>
/// Snapshot tổng hợp toàn bộ Site cho Dashboard Admin/Manager (system-wide —
/// khác <see cref="SiteDashboardDto"/> là dashboard của MỘT site).
/// Không nhận tham số thời gian — luôn phản ánh trạng thái hiện tại.
/// </summary>
public class SiteDashboardStatsDto
{
    /// <summary>Tổng số site (không tính đã xóa).</summary>
    public int Total { get; set; }

    /// <summary>Số site đang Active.</summary>
    public int ActiveCount { get; set; }

    /// <summary>Tổng số battery asset toàn hệ thống (gồm cả asset chưa gán site) — khớp <c>TotalAssets</c> của <c>/api/battery/dashboard/stats</c>.</summary>
    public int TotalBatteries { get; set; }

    /// <summary>Số battery asset đang Active toàn hệ thống — khớp <c>ActiveAssets</c> của <c>/api/battery/dashboard/stats</c>.</summary>
    public int ActiveBatteries { get; set; }

    /// <summary>Trung bình health score của các site (công thức giống <c>GET /api/sites/{id}/dashboard</c>; site chưa có asset = 100). Làm tròn 2 chữ số; 0 khi chưa có site.</summary>
    public double AvgHealth { get; set; }

    /// <summary>Số site có health score &lt; 80 — "cần chú ý".</summary>
    public int AtRiskCount { get; set; }
}
