using AuditAggregatorService.Application.CQRS.Query.Audit;
using AuditAggregatorService.Application.DTOs;
using AuditAggregatorService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuditAggregatorService.Application.CQRS.Handler.Audit;

public class AuditGetAccountTimelineQueryHandler
    : IRequestHandler<AuditGetAccountTimelineQuery, CommonResponse<List<AuditAggregateDto>>>
{
    private readonly IAuditAggregatorUnitOfWork _unitOfWork;
    public AuditGetAccountTimelineQueryHandler(IAuditAggregatorUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<List<AuditAggregateDto>>> Handle(
        AuditGetAccountTimelineQuery request, CancellationToken ct)
    {
        var capped = request.Limit is <= 0 or > 500 ? 100 : request.Limit;

        var items = await _unitOfWork.AuditAggregates.GetAllAsync(tracking: false)
            .Where(x => x.ActorAccountId == request.AccountId || x.TargetId == request.AccountId)
            .OrderByDescending(x => x.OccurredAt)
            .Take(capped)
            .Select(x => AuditAggregateDto.FromEntity(x))
            .ToListAsync(ct);

        return new CommonResponse<List<AuditAggregateDto>> { IsSuccess = true, StatusCode = 200, Data = items };
    }
}
