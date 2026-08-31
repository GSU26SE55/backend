using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Reports;
using TicketService.Application.DTOs.Response.Reports;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Reports;

public class StaffPerformanceReportQueryHandler
    : IRequestHandler<StaffPerformanceReportQuery, CommonResponse<List<StaffPerformanceRow>>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ISlaCalculator _slaCalculator;

    public StaffPerformanceReportQueryHandler(ITicketUnitOfWork uow, ISlaCalculator slaCalculator)
    {
        _uow = uow;
        _slaCalculator = slaCalculator;
    }

    public async Task<CommonResponse<List<StaffPerformanceRow>>> Handle(StaffPerformanceReportQuery request, CancellationToken ct)
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
                AssignmentCreatedAt = a.CreatedAt,
                t.Status,
                t.CreatedAt,
                t.ResolvedAt,
                t.Rating,
                SlaStatus = t.SlaTimer != null ? (SlaTimerStatusEnum?)t.SlaTimer.Status : null,
                SlaBreachAt = t.SlaTimer != null ? t.SlaTimer.BreachAt : (DateTime?)null
            }
        ).ToListAsync(ct);

        var staff = await _uow.StaffAccounts.GetAllAsync().AsNoTracking()
            .Select(s => new { s.Id, s.AccountId, s.FullName }).ToListAsync(ct);
        var names = new Dictionary<Guid, string>();
        foreach (var s in staff)
        {
            names[s.Id] = s.FullName;
            names[s.AccountId] = s.FullName;
        }

        var rows = data.GroupBy(x => x.StaffId).Select(g =>
        {
            var resolved = g.Where(x => TicketReportHelpers.ResolvedStatuses.Contains(x.Status) && x.ResolvedAt != null).ToList();
            var met = g.Count(x => x.SlaStatus == SlaTimerStatusEnum.Met);
            var breached = g.Count(x => x.SlaStatus == SlaTimerStatusEnum.Breached);
            var ratings = g.Where(x => x.Rating != null).Select(x => (decimal)x.Rating!.Value).ToList();

            // Rescue KPI: PrimaryHandler assigned AFTER the breach and then resolved the ticket
            var rescueAttempts = resolved
                .Where(x => x.SlaStatus == SlaTimerStatusEnum.Breached
                            && x.SlaBreachAt.HasValue
                            && x.AssignmentCreatedAt >= x.SlaBreachAt.Value)
                .ToList();
            var rescueSuccessCount = rescueAttempts
                .Count(x => (int)_slaCalculator.GetWorkingMinutesBetween(
                    x.AssignmentCreatedAt, x.ResolvedAt!.Value) <= 1440);

            return new StaffPerformanceRow
            {
                StaffId = g.Key.ToString(),
                Name = names.TryGetValue(g.Key, out var n) ? n : null,
                TicketsResolved = resolved.Count,
                AvgResolveHours = resolved.Count == 0 ? 0m
                    : Math.Round((decimal)resolved.Average(x => (x.ResolvedAt!.Value - x.CreatedAt).TotalHours), 2),
                AvgRating = ratings.Count == 0 ? null : Math.Round(ratings.Average(), 2),
                SlaCompliance = TicketReportHelpers.Compliance(met, breached),
                RescueCount = rescueAttempts.Count,
                RescueSuccessCount = rescueSuccessCount
            };
        }).OrderByDescending(r => r.TicketsResolved).ToList();

        return new CommonResponse<List<StaffPerformanceRow>> { IsSuccess = true, StatusCode = 200, Data = rows };
    }
}
