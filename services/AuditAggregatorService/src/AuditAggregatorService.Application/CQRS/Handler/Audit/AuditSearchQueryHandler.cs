using AuditAggregatorService.Application.CQRS.Query.Audit;
using AuditAggregatorService.Application.DTOs;
using AuditAggregatorService.Application.Interfaces;
using MediatR;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;

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

        // Phân trang trên entity rồi mới map: AuditAggregateDto.FromEntity là method call, EF không
        // dịch được sang SQL — chiếu trước khi cắt trang sẽ làm Skip/Take mất khả năng dịch.
        var page = await query
            .OrderByDescending(x => x.OccurredAt)
            .ThenBy(x => x.Id) // tie-breaker cố định — pagination ổn định
            .ToPagedEntityListAsync(request.PageNumber, request.PageSize, ct);

        return new CommonResponse<PaginationResponse<AuditAggregateDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page.Map(AuditAggregateDto.FromEntity),
        };
    }
}
