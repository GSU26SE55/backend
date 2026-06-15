using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Sprint 5B #104 — publish khi EnvironmentalIncident được report (site-level).
/// Subscribers: NotificationService (notify Manager/Admin), TicketService.
///
/// Shape align với §1.7: bao gồm <c>CustomerId</c> + <c>SiteName</c> để consumer
/// notify mà không phải gọi back BatteryService; <c>Description</c> giúp template
/// notification render context.
/// </summary>
public record EnvironmentalIncidentDetectedEvent(
    Guid IncidentId,
    Guid SiteId,
    Guid CustomerId,
    string SiteName,
    int IncidentType,
    int Severity,
    DateTime DetectedAt,
    Guid AlertId,
    string? Description
) : IntegrationEvent;

/// <summary>
/// Sprint 5B #104 — publish khi EnvironmentalIncident chuyển sang Resolved hoặc FalseAlarm.
///
/// Shape align với §1.7: <c>ResolvedByUserId</c> + <c>WasFalseAlarm</c> boolean
/// (thay vì chuỗi "Resolution") để consumer phân biệt false-alarm vs genuine resolution
/// rõ ràng cho audit/report.
/// </summary>
public record EnvironmentalIncidentResolvedEvent(
    Guid IncidentId,
    Guid SiteId,
    DateTime ResolvedAt,
    Guid? ResolvedByUserId,
    bool WasFalseAlarm,
    string? ResolutionNote
) : IntegrationEvent;
