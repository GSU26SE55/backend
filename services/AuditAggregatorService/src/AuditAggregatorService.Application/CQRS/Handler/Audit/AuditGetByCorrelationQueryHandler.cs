using AuditAggregatorService.Application.CQRS.Query.Audit;
using AuditAggregatorService.Application.DTOs;
using AuditAggregatorService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuditAggregatorService.Application.CQRS.Handler.Audit;

public class AuditGetByCorrelationQueryHandler
    : IRequestHandler<AuditGetByCorrelationQuery, CommonResponse<List<AuditAggregateDto>>>
{
    private readonly IAuditAggregatorUnitOfWork _unitOfWork;
    public AuditGetByCorrelationQueryHandler(IAuditAggregatorUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<List<AuditAggregateDto>>> Handle(
        AuditGetByCorrelationQuery request, CancellationToken ct)
    {
        var items = await _unitOfWork.AuditAggregates.GetAllAsync(tracking: false)
            .Where(x => x.CorrelationId == request.CorrelationId)
            .OrderBy(x => x.OccurredAt)
            .Select(x => AuditAggregateDto.FromEntity(x))
            .ToListAsync(ct);

        return new CommonResponse<List<AuditAggregateDto>> { IsSuccess = true, StatusCode = 200, Data = items };
    }
}
