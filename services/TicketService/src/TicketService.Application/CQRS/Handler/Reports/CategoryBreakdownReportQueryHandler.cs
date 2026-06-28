using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Reports;
using TicketService.Application.DTOs.Response.Reports;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Reports;

public class CategoryBreakdownReportQueryHandler
    : IRequestHandler<CategoryBreakdownReportQuery, CommonResponse<List<CategoryBreakdownRow>>>
{
    private readonly ITicketUnitOfWork _uow;
    public CategoryBreakdownReportQueryHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<List<CategoryBreakdownRow>>> Handle(CategoryBreakdownReportQuery request, CancellationToken ct)
    {
        var q = _uow.Tickets.GetAllAsync().AsNoTracking().Where(t => !t.IsDeleted);
        if (request.From.HasValue)
            q = q.Where(t => t.CreatedAt >= request.From.Value);
        if (request.To.HasValue)
            q = q.Where(t => t.CreatedAt <= request.To.Value);

        var data = await q.GroupBy(t => t.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() }).ToListAsync(ct);

        var rows = data.OrderByDescending(x => x.Count)
            .Select(x => new CategoryBreakdownRow { Category = x.Category.ToString(), Count = x.Count }).ToList();

        return new CommonResponse<List<CategoryBreakdownRow>> { IsSuccess = true, StatusCode = 200, Data = rows };
    }
}
