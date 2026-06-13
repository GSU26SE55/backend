using System.ComponentModel.DataAnnotations;

namespace BatteryService.Application.DTOs;

/// <summary>
/// Body cho <c>POST /api/environmental-incidents/{id}/resolve</c>.
/// </summary>
public class ResolveEnvironmentalIncidentRequest
{
    /// <summary>
    /// Mô tả cách xử lý sự cố — bắt buộc, 5–2000 ký tự (audit trail).
    /// </summary>
    /// <example>Đã bơm thoát nước cabinet, đo điện trở cách điện đạt chuẩn.</example>
    [Required]
    [StringLength(2000, MinimumLength = 5)]
    public string ResolutionNote { get; set; } = string.Empty;
}

/// <summary>
/// Body cho <c>POST /api/environmental-incidents/{id}/false-alarm</c>.
/// </summary>
public class FalseAlarmEnvironmentalIncidentRequest
{
    /// <summary>
    /// Lý do đánh dấu false-alarm — bắt buộc, 5–2000 ký tự.
    /// </summary>
    /// <example>Vệ sinh module bằng cồn — bay hơi giả gas leak.</example>
    [Required]
    [StringLength(2000, MinimumLength = 5)]
    public string FalseAlarmReason { get; set; } = string.Empty;
}
