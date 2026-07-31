using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Reports;
using TicketService.Application.DTOs.Response.Reports;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Reports;

public class SlaByStaffReportQueryHandler
    : IRequestHandler<SlaByStaffReportQuery, CommonResponse<List<SlaByStaffRow>>>
{
    private readonly ITicketUnitOfWork _uow;
    public SlaByStaffReportQueryHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<List<SlaByStaffRow>>> Handle(SlaByStaffReportQuery request, CancellationToken ct)
    {
        var ticketsBase = _uow.Tickets.GetAllAsync().AsNoTracking()
            .Where(t => !t.IsDeleted);
        if (request.From.HasValue)
            ticketsBase = ticketsBase.Where(t => t.CreatedAt >= request.From.Value);
        if (request.To.HasValue)
            ticketsBase = ticketsBase.Where(t => t.CreatedAt <= request.To.Value);

        var assignmentsBase = _uow.TicketAssignments.GetAllAsync().AsNoTracking()
            .Where(a => !a.IsDeleted && a.Role == AssignmentRoleEnum.PrimaryHandler);

        var data = await (
            from a in assignmentsBase
            join t in ticketsBase on a.TicketId equals t.Id
            select new
            {
                a.StaffId,
                Status = t.SlaTimer != null ? (SlaTimerStatusEnum?)t.SlaTimer.Status : null
            }
        ).ToListAsync(ct);

        var names = await StaffNameLookup(ct);

        var rows = data.GroupBy(x => x.StaffId).Select(g =>
        {
            var met = g.Count(x => x.Status == SlaTimerStatusEnum.Met);
            var breached = g.Count(x => x.Status == SlaTimerStatusEnum.Breached);
            return new SlaByStaffRow
            {
                StaffId = g.Key.ToString(),
                Name = names.TryGetValue(g.Key, out var n) ? n : null,
                TotalAssigned = g.Count(),
                Met = met,
                Breached = breached,
                ComplianceRate = TicketReportHelpers.Compliance(met, breached)
            };
        }).OrderByDescending(r => r.TotalAssigned).ToList();

        return new CommonResponse<List<SlaByStaffRow>> { IsSuccess = true, StatusCode = 200, Data = rows };
    }

    private async Task<Dictionary<Guid, string>> StaffNameLookup(CancellationToken ct)
    {
        var staff = await _uow.StaffAccounts.GetAllAsync().AsNoTracking()
            .Select(s => new { s.Id, s.AccountId, s.FullName }).ToListAsync(ct);
        var dict = new Dictionary<Guid, string>();
        foreach (var s in staff)
        {
            dict[s.Id] = s.FullName;
            dict[s.AccountId] = s.FullName;   // AssignedStaffId có thể là Id hoặc AccountId
        }
        return dict;
    }
}
