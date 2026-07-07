using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Ticket;

public class TicketDashboardStatsQueryHandler
    : IRequestHandler<TicketDashboardStatsQuery, CommonResponse<TicketDashboardStatsDto>>
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public TicketDashboardStatsQueryHandler(ITicketUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<TicketDashboardStatsDto>> Handle(
        TicketDashboardStatsQuery request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var ticketsQuery = _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => !t.IsDeleted);

        // ===== Count theo status — zero-fill đủ 14 status để FE không phải phòng thủ key thiếu =====
        var statusGroups = await ticketsQuery
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var countByStatus = Enum.GetValues<TicketStatusEnum>()
            .ToDictionary(s => s.ToString(), _ => 0);
        foreach (var group in statusGroups)
            countByStatus[group.Status.ToString()] = group.Count;

        var total = statusGroups.Sum(g => g.Count);
        var openCount = statusGroups
            .Where(g => !TicketStatusGroups.Terminal.Contains(g.Status))
            .Sum(g => g.Count);

        // ===== Count theo priority — ticket chưa triage (Priority null) không tính =====
        var priorityGroups = await ticketsQuery
            .Where(t => t.Priority != null)
            .GroupBy(t => t.Priority!.Value)
            .Select(g => new { Priority = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var countByPriority = Enum.GetValues<TicketPriorityEnum>()
            .ToDictionary(p => p.ToString(), _ => 0);
        foreach (var group in priorityGroups)
            countByPriority[group.Priority.ToString()] = group.Count;

        // ===== SLA summary theo SlaTimer.Status =====
        var slaGroups = await ticketsQuery
            .Where(t => t.SlaTimer != null)
            .GroupBy(t => t.SlaTimer!.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var sla = new SlaSummaryDto
        {
            Met = slaGroups.FirstOrDefault(g => g.Status == SlaTimerStatusEnum.Met)?.Count ?? 0,
            Breached = slaGroups.FirstOrDefault(g => g.Status == SlaTimerStatusEnum.Breached)?.Count ?? 0,
            Running = slaGroups.FirstOrDefault(g => g.Status == SlaTimerStatusEnum.Running)?.Count ?? 0,
            Paused = slaGroups.FirstOrDefault(g => g.Status == SlaTimerStatusEnum.Paused)?.Count ?? 0
        };
        var completedTimers = sla.Met + sla.Breached;
        sla.CompliancePercent = completedTimers == 0
            ? 100d
            : Math.Round(sla.Met * 100d / completedTimers, 2);

        // ===== Trend 7 ngày theo CreatedAt (bucket UTC, ngày trống = 0) =====
        var trendFrom = now.Date.AddDays(-6);
        var trendRaw = await ticketsQuery
            .Where(t => t.CreatedAt >= trendFrom)
            .Select(t => t.CreatedAt.Date)
            .ToListAsync(cancellationToken);
        var trendByDay = trendRaw
            .GroupBy(d => d)
            .ToDictionary(g => g.Key, g => g.Count());
        var createdTrend = new List<DailyCountPointDto>();
        for (var i = 0; i < 7; i++)
        {
            var day = trendFrom.AddDays(i);
            createdTrend.Add(new DailyCountPointDto
            {
                Date = DateOnly.FromDateTime(day),
                Count = trendByDay.GetValueOrDefault(day)
            });
        }

        // ===== Workload: số ticket mở theo staff =====
        var openByStaffGroups = await ticketsQuery
            .Where(t => t.AssignedStaffId != null && !TicketStatusGroups.Terminal.Contains(t.Status))
            .GroupBy(t => t.AssignedStaffId!.Value)
            .Select(g => new { StaffId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var openCountByStaff = openByStaffGroups
            .OrderByDescending(g => g.Count)
            .Select(g => new StaffOpenCountDto
            {
                StaffId = g.StaffId.ToString(),
                ActiveCount = g.Count
            })
            .ToList();

        return new CommonResponse<TicketDashboardStatsDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new TicketDashboardStatsDto
            {
                Total = total,
                OpenCount = openCount,
                Sla = sla,
                CountByStatus = countByStatus,
                CountByPriority = countByPriority,
                CreatedTrend7Days = createdTrend,
                OpenCountByStaff = openCountByStaff
            }
        };
    }
}
