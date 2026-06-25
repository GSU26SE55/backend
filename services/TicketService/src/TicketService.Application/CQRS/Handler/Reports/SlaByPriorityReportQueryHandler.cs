using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Reports;
using TicketService.Application.DTOs.Response.Reports;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Reports;

public class SlaByPriorityReportQueryHandler
    : IRequestHandler<SlaByPriorityReportQuery, CommonResponse<List<SlaByPriorityRow>>>
{
    private readonly ITicketUnitOfWork _uow;
    public SlaByPriorityReportQueryHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<List<SlaByPriorityRow>>> Handle(SlaByPriorityReportQuery request, CancellationToken ct)
    {
        var q = _uow.SlaTimers.GetAllAsync().AsNoTracking().Where(s => !s.IsDeleted);
        if (request.From.HasValue)
            q = q.Where(s => s.StartedAt >= request.From.Value);
        if (request.To.HasValue)
            q = q.Where(s => s.StartedAt <= request.To.Value);

        var data = await q.Select(s => new { s.Priority, s.Status }).ToListAsync(ct);

        var rows = new[] { TicketPriorityEnum.P1Critical, TicketPriorityEnum.P2High, TicketPriorityEnum.P3Normal }
            .Select(p =>
            {
                var grp = data.Where(x => x.Priority == p).ToList();
                var met = grp.Count(x => x.Status == SlaTimerStatusEnum.Met);
                var breached = grp.Count(x => x.Status == SlaTimerStatusEnum.Breached);
                return new SlaByPriorityRow
                {
                    Priority = p.ToString(),
                    Met = met,
                    Breached = breached,
                    Total = grp.Count,
                    ComplianceRate = TicketReportHelpers.Compliance(met, breached)
                };
            }).ToList();

        return new CommonResponse<List<SlaByPriorityRow>> { IsSuccess = true, StatusCode = 200, Data = rows };
    }
}
