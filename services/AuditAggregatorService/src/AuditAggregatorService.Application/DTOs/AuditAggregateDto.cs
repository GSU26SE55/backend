using AuditAggregatorService.Domain.Entities;

namespace AuditAggregatorService.Application.DTOs;

/// <summary>Output DTO cho 1 audit record (Sprint audit <c>#AUDIT-17</c>). Guid → string theo convention repo.</summary>
public class AuditAggregateDto
{
    public string Id { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string ActionCode { get; set; } = string.Empty;
    public string ActionCategory { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? TargetDisplay { get; set; }
    public string? ActorAccountId { get; set; }
    public string? ActorRole { get; set; }
    public string? ActorDisplay { get; set; }
    public string? ActorIp { get; set; }
    public string? ActorUserAgent { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorCode { get; set; }
    public string? Reason { get; set; }
    public string? MetadataJson { get; set; }
    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime RecordedAt { get; set; }
    public string? GeoCountry { get; set; }
    public string? GeoCity { get; set; }

    public static AuditAggregateDto FromEntity(AuditAggregate x) => new()
    {
        Id = x.Id.ToString(),
        EventId = x.EventId.ToString(),
        ServiceName = x.ServiceName,
        ActionCode = x.ActionCode,
        ActionCategory = x.ActionCategory,
        Severity = x.Severity,
        TargetType = x.TargetType,
        TargetId = x.TargetId?.ToString(),
        TargetDisplay = x.TargetDisplay,
        ActorAccountId = x.ActorAccountId?.ToString(),
        ActorRole = x.ActorRole,
        ActorDisplay = x.ActorDisplay,
        ActorIp = x.ActorIp,
        ActorUserAgent = x.ActorUserAgent,
        IsSuccess = x.IsSuccess,
        ErrorCode = x.ErrorCode,
        Reason = x.Reason,
        MetadataJson = x.MetadataJson,
        CorrelationId = x.CorrelationId?.ToString(),
        CausationId = x.CausationId?.ToString(),
        OccurredAt = x.OccurredAt,
        RecordedAt = x.RecordedAt,
        GeoCountry = x.GeoCountry,
        GeoCity = x.GeoCity,
    };
}
