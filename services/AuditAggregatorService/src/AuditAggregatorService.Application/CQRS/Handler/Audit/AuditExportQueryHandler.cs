using AuditAggregatorService.Application.CQRS.Query.Audit;
using AuditAggregatorService.Application.DTOs;
using AuditAggregatorService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuditAggregatorService.Application.CQRS.Handler.Audit;

/// <summary>
/// Trả luồng <see cref="IAsyncEnumerable{T}"/> theo filter cho export streaming (không phân trang, tránh OOM).
/// Việc serialize (CSV/JSON) + ghi xuống Response do controller đảm nhiệm — file download nên không bọc CommonResponse.
/// </summary>
public class AuditExportQueryHandler : IRequestHandler<AuditExportQuery, IAsyncEnumerable<AuditAggregateDto>>
{
    private readonly IAuditAggregatorUnitOfWork _unitOfWork;
    public AuditExportQueryHandler(IAuditAggregatorUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public Task<IAsyncEnumerable<AuditAggregateDto>> Handle(AuditExportQuery request, CancellationToken ct)
    {
        var stream = _unitOfWork.AuditAggregates.GetAllAsync(tracking: false)
            .ApplyFilters(request)
            .OrderByDescending(x => x.OccurredAt)
            .Select(x => AuditAggregateDto.FromEntity(x))
            .AsAsyncEnumerable();

        return Task.FromResult(stream);
    }
}
