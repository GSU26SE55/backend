using AuditAggregatorService.Application.DTOs;
using AuditAggregatorService.Domain.Entities;

namespace AuditAggregatorService.Application.CQRS.Query.Audit;

/// <summary>Áp filter chung cho Search + Export handler (tránh trùng lặp).</summary>
internal static class AuditAggregateQueryExtensions
{
    public static IQueryable<AuditAggregate> ApplyFilters(this IQueryable<AuditAggregate> q, AuditSearchRequest r)
    {
        if (!string.IsNullOrWhiteSpace(r.Service))
            q = q.Where(x => x.ServiceName == r.Service);
        if (!string.IsNullOrWhiteSpace(r.Action))
            q = q.Where(x => x.ActionCode == r.Action);
        if (!string.IsNullOrWhiteSpace(r.Category))
            q = q.Where(x => x.ActionCategory == r.Category);
        if (!string.IsNullOrWhiteSpace(r.Severity))
            q = q.Where(x => x.Severity == r.Severity);
        if (r.ActorId.HasValue)
            q = q.Where(x => x.ActorAccountId == r.ActorId);
        if (r.TargetId.HasValue)
            q = q.Where(x => x.TargetId == r.TargetId);
        if (r.CorrelationId.HasValue)
            q = q.Where(x => x.CorrelationId == r.CorrelationId);
        if (r.IsSuccess.HasValue)
            q = q.Where(x => x.IsSuccess == r.IsSuccess);
        if (r.From.HasValue)
            q = q.Where(x => x.OccurredAt >= r.From.Value);
        if (r.To.HasValue)
            q = q.Where(x => x.OccurredAt <= r.To.Value);
        return q;
    }
}
