using SharedContracts.Common.Requests;

namespace AuditAggregatorService.Application.DTOs;

/// <summary>Filter + pagination cho <c>GET /api/admin/audit/search</c> (Sprint audit <c>#AUDIT-17</c>).</summary>
public class AuditSearchRequest : PaginationRequest
{
    public string? Service { get; set; }
    public string? Action { get; set; }
    public string? Category { get; set; }
    public string? Severity { get; set; }
    public Guid? ActorId { get; set; }
    public Guid? TargetId { get; set; }
    public Guid? CorrelationId { get; set; }
    public bool? IsSuccess { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

/// <summary>1 dòng thống kê count theo nhóm (<c>GET /api/admin/audit/stats</c>).</summary>
public class AuditStatsItemDto
{
    public string Key { get; set; } = string.Empty;
    public long Count { get; set; }
}
