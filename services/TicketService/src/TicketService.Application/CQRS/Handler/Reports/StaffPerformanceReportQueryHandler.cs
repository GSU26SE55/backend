using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Reports;
using TicketService.Application.DTOs.Response.Reports;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Reports;

public class StaffPerformanceReportQueryHandler
    : IRequestHandler<StaffPerformanceReportQuery, CommonResponse<List<StaffPerformanceRow>>>
{
    private readonly ITicketUnitOfWork _uow;
    public StaffPerformanceReportQueryHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<List<StaffPerformanceRow>>> Handle(StaffPerformanceReportQuery request, CancellationToken ct)
    {
        var q = _uow.Tickets.GetAllAsync().AsNoTracking().Include(t => t.SlaTimer)
            .Where(t => !t.IsDeleted && t.AssignedStaffId != null);
        if (request.From.HasValue)
            q = q.Where(t => t.CreatedAt >= request.From.Value);
        if (request.To.HasValue)
            q = q.Where(t => t.CreatedAt <= request.To.Value);

        var data = await q.Select(t => new
        {
            StaffId = t.AssignedStaffId!.Value,
            t.Status,
            t.CreatedAt,
            t.ResolvedAt,
            t.Rating,
            SlaStatus = t.SlaTimer != null ? (SlaTimerStatusEnum?)t.SlaTimer.Status : null
        }).ToListAsync(ct);

        var staff = await _uow.StaffAccounts.GetAllAsync().AsNoTracking()
            .Select(s => new { s.Id, s.AccountId, s.FullName }).ToListAsync(ct);
        var names = new Dictionary<Guid, string>();
        foreach (var s in staff)
<<<<<<< HEAD
        {
            names[s.Id] = s.FullName;
            names[s.AccountId] = s.FullName;
        }
=======
        { names[s.Id] = s.FullName; names[s.AccountId] = s.FullName; }
>>>>>>> 86174650b710f709794126352a88437567dd4f88

        var rows = data.GroupBy(x => x.StaffId).Select(g =>
        {
            var resolved = g.Where(x => TicketReportHelpers.ResolvedStatuses.Contains(x.Status) && x.ResolvedAt != null).ToList();
            var met = g.Count(x => x.SlaStatus == SlaTimerStatusEnum.Met);
            var breached = g.Count(x => x.SlaStatus == SlaTimerStatusEnum.Breached);
            var ratings = g.Where(x => x.Rating != null).Select(x => (decimal)x.Rating!.Value).ToList();

            return new StaffPerformanceRow
            {
                StaffId = g.Key.ToString(),
                Name = names.TryGetValue(g.Key, out var n) ? n : null,
                TicketsResolved = resolved.Count,
                AvgResolveHours = resolved.Count == 0 ? 0m
                    : Math.Round((decimal)resolved.Average(x => (x.ResolvedAt!.Value - x.CreatedAt).TotalHours), 2),
                AvgRating = ratings.Count == 0 ? null : Math.Round(ratings.Average(), 2),
                SlaCompliance = TicketReportHelpers.Compliance(met, breached)
            };
        }).OrderByDescending(r => r.TicketsResolved).ToList();

        return new CommonResponse<List<StaffPerformanceRow>> { IsSuccess = true, StatusCode = 200, Data = rows };
    }
}
