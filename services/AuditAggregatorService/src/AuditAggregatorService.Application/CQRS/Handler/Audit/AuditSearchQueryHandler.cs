using AuditAggregatorService.Application.CQRS.Query.Audit;
using AuditAggregatorService.Application.DTOs;
using AuditAggregatorService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuditAggregatorService.Application.CQRS.Handler.Audit;

public class AuditSearchQueryHandler
    : IRequestHandler<AuditSearchQuery, CommonResponse<PaginationResponse<AuditAggregateDto>>>
{
    private readonly IAuditAggregatorUnitOfWork _unitOfWork;
    public AuditSearchQueryHandler(IAuditAggregatorUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<PaginationResponse<AuditAggregateDto>>> Handle(
        AuditSearchQuery request, CancellationToken ct)
    {
        // E (A+E, #AUDIT-17): validate filter tập-đóng severity/category → 400 + listErrors thay vì 200 rỗng âm thầm.
        var validationErrors = AuditSearchValidator.Validate(request);
        if (validationErrors.Count > 0)
        {
            return new CommonResponse<PaginationResponse<AuditAggregateDto>>
            {
                IsSuccess = false,
                StatusCode = 400,
                ListErrors = validationErrors,
            };
        }

        var query = _unitOfWork.AuditAggregates.GetAllAsync(tracking: false).ApplyFilters(request);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.OccurredAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => AuditAggregateDto.FromEntity(x))
            .ToListAsync(ct);

        var page = new PaginationResponse<AuditAggregateDto>
        {
            Items = items,
            TotalItems = total,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
        };

        return new CommonResponse<PaginationResponse<AuditAggregateDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page,
        };
    }
}
