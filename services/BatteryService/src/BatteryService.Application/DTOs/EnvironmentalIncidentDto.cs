using BatteryService.Domain.Enums;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.DTOs;

public class EnvironmentalIncidentDto
{
    /// <summary>Định danh resource.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>ID Site (Guid).</summary>
    public string SiteId { get; set; } = string.Empty;
    /// <summary>Loại incident (SmokeDetected | WaterLeak | ...).</summary>
    public EnvironmentalIncidentTypeEnum IncidentType { get; set; }
    /// <summary>Filter theo status enum.</summary>
    public EnvironmentalIncidentStatusEnum Status { get; set; }
    /// <summary>Severity của alert (Warning | Critical).</summary>
    public AlertSeverityEnum Severity { get; set; }
    /// <summary>User ID đã report incident.</summary>
    public string? ReportedBy { get; set; }
    /// <summary>
    /// Số đo cảm biến do firmware gửi kèm — ví dụ <c>"MQ-2 raw=3100 > thr=2000 (GPIO1)"</c>.
    ///
    /// <para>Đây là BẰNG CHỨNG của sự cố cấp site, tương đương bảng số đo của ticket pin. Trước
    /// đây trường này được lưu vào entity nhưng không map ra DTO, nên UI chỉ còn cách đọc câu mô
    /// tả tự sinh của ticket — vốn trộn lẫn số đo với văn bản khuôn mẫu và mã enum thô
    /// (<c>"type 3, severity 3"</c>). Tách riêng ở đây để UI trình bày số đo đúng như nó vốn là.</para>
    /// </summary>
    public string? Notes { get; set; }
    /// <summary>Timestamp phát hiện (UTC).</summary>
    public DateTime DetectedAt { get; set; }
    /// <summary>Timestamp acknowledged (UTC).</summary>
    public DateTime? AcknowledgedAt { get; set; }
    /// <summary>Timestamp resolved (UTC).</summary>
    public DateTime? ResolvedAt { get; set; }
    /// <summary>Ghi chú khi resolve.</summary>
    public string? ResolutionNote { get; set; }
    /// <summary>Timestamp (UTC).</summary>
    public DateTime? FalseAlarmAt { get; set; }
    /// <summary>Field FalseAlarmReason.</summary>
    public string? FalseAlarmReason { get; set; }
    /// <summary>Timestamp tạo (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}

public class EnvironmentalIncidentResponse : CommonResponse<EnvironmentalIncidentDto> { }
public class EnvironmentalIncidentListResponse : CommonResponse<PaginationResponse<EnvironmentalIncidentDto>> { }
