namespace BatteryService.Application.DTOs;

/// <summary>Sprint 7 B4 (§31.7) — mức rủi ro lan truyền derive từ score.</summary>
public enum CascadeRiskLevel
{
    Low = 1,      // < 0.5
    Medium = 2,   // 0.5 – < 0.7
    High = 3      // >= 0.7
}
