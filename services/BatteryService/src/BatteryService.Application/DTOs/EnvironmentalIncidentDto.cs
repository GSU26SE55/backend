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
