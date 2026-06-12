using BatteryService.Domain.Enums;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.DTOs;

public class EnvironmentalIncidentDto
{
    public string Id { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public EnvironmentalIncidentTypeEnum IncidentType { get; set; }
    public EnvironmentalIncidentStatusEnum Status { get; set; }
    public AlertSeverityEnum Severity { get; set; }
    public string? ReportedBy { get; set; }
    public DateTime DetectedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime? FalseAlarmAt { get; set; }
    public string? FalseAlarmReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class EnvironmentalIncidentResponse : CommonResponse<EnvironmentalIncidentDto> { }
public class EnvironmentalIncidentListResponse : CommonResponse<PaginationResponse<EnvironmentalIncidentDto>> { }
