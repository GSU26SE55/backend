using AuditAggregatorService.Application.CQRS.Query.Audit;
using AuditAggregatorService.Application.DTOs;
using AuditAggregatorService.Application.Interfaces;
using AuditAggregatorService.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuditAggregatorService.Application.CQRS.Handler.Audit;

public class AuditGetStatsQueryHandler
    : IRequestHandler<AuditGetStatsQuery, CommonResponse<List<AuditStatsItemDto>>>
{
    private readonly IAuditAggregatorUnitOfWork _unitOfWork;
    public AuditGetStatsQueryHandler(IAuditAggregatorUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<List<AuditStatsItemDto>>> Handle(AuditGetStatsQuery request, CancellationToken ct)
    {
        var q = _unitOfWork.AuditAggregates.GetAllAsync(tracking: false);
        if (request.From.HasValue)
            q = q.Where(x => x.OccurredAt >= request.From.Value);
        if (request.To.HasValue)
            q = q.Where(x => x.OccurredAt <= request.To.Value);

        IQueryable<IGrouping<string, AuditAggregate>> grouped = request.GroupBy?.ToLowerInvariant() switch
        {
            "service" => q.GroupBy(x => x.ServiceName),
            "action" => q.GroupBy(x => x.ActionCode),
            _ => q.GroupBy(x => x.Severity), // default: severity
        };

        var stats = await grouped
            .Select(g => new AuditStatsItemDto { Key = g.Key, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(ct);

        return new CommonResponse<List<AuditStatsItemDto>> { IsSuccess = true, StatusCode = 200, Data = stats };
    }
}
