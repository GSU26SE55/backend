namespace BatteryService.Application.DTOs;

public class SiteDashboardDto
{
    /// <summary>ID Site (Guid).</summary>
    public string SiteId { get; set; } = string.Empty;

    /// <summary>Tên hiển thị.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>ID Customer (Guid).</summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>Tổng số asset trong scope.</summary>
    public int TotalAssets { get; set; }

    /// <summary>Số asset đang active.</summary>
    public int ActiveAssets { get; set; }

    /// <summary>Số asset có alert active.</summary>
    public int AssetsWithActiveAlerts { get; set; }

    /// <summary>Timestamp alert gần nhất tại site.</summary>
    public DateTime? LastAlertAt { get; set; }

    /// <summary>Health score 0..100 — tổng hợp SOH + alert + uptime.</summary>
    public int HealthScore { get; set; }
}
