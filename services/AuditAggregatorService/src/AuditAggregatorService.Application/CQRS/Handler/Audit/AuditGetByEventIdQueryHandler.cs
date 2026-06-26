using AuditAggregatorService.Application.CQRS.Query.Audit;
using AuditAggregatorService.Application.DTOs;
using AuditAggregatorService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuditAggregatorService.Application.CQRS.Handler.Audit;

public class AuditGetByEventIdQueryHandler
    : IRequestHandler<AuditGetByEventIdQuery, CommonResponse<AuditAggregateDto>>
{
    private readonly IAuditAggregatorUnitOfWork _unitOfWork;
    public AuditGetByEventIdQueryHandler(IAuditAggregatorUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<AuditAggregateDto>> Handle(AuditGetByEventIdQuery request, CancellationToken ct)
    {
        var entity = await _unitOfWork.AuditAggregates.GetAllAsync(tracking: false)
            .FirstOrDefaultAsync(x => x.EventId == request.EventId, ct);

        if (entity is null)
            return new CommonResponse<AuditAggregateDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy audit event.",
            };

        return new CommonResponse<AuditAggregateDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = AuditAggregateDto.FromEntity(entity),
        };
    }
}
