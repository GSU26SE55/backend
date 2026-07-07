using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Ticket;

public class MyTicketDashboardStatsAsStaffQueryHandler
    : IRequestHandler<MyTicketDashboardStatsAsStaffQuery, CommonResponse<StaffTicketDashboardStatsDto>>
{
    /// <summary>Ngưỡng "sắp breach": timer Running còn ≤ 25% thời gian (khớp KPI "Sắp breach ≤25%" phía FE).</summary>
    private const double NearBreachThresholdPercent = 25d;

    private readonly ITicketUnitOfWork _unitOfWork;
    private readonly ITicketCurrentUserService _currentUserService;

    public MyTicketDashboardStatsAsStaffQueryHandler(
        ITicketUnitOfWork unitOfWork, ITicketCurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<StaffTicketDashboardStatsDto>> Handle(
        MyTicketDashboardStatsAsStaffQuery request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var staffId))
        {
            return new CommonResponse<StaffTicketDashboardStatsDto>
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Chưa đăng nhập."
            };
        }

        var now = DateTime.UtcNow;
        var ticketsQuery = _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => !t.IsDeleted && t.AssignedStaffId == staffId);

        // ===== Count theo status — zero-fill đủ 14 status =====
        var statusGroups = await ticketsQuery
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var countByStatus = Enum.GetValues<TicketStatusEnum>()
            .ToDictionary(s => s.ToString(), _ => 0);
        foreach (var group in statusGroups)
            countByStatus[group.Status.ToString()] = group.Count;

        var openCount = statusGroups
            .Where(g => !TicketStatusGroups.Terminal.Contains(g.Status))
            .Sum(g => g.Count);
        var resolvedCount = statusGroups
            .Where(g => TicketStatusGroups.ResolvedGroup.Contains(g.Status))
            .Sum(g => g.Count);

        // ===== SLA summary trên toàn bộ ticket được gán cho staff =====
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

        // ===== Rủi ro SLA trên nhóm đang theo dõi =====
        // Cần tính RemainingPercent (phụ thuộc UtcNow) nên fetch các trường timer về memory —
        // tập monitored giới hạn theo 1 staff nên khối lượng nhỏ.
        var monitoredTimers = await ticketsQuery
            .Where(t => TicketStatusGroups.SlaMonitored.Contains(t.Status) && t.SlaTimer != null)
            .Select(t => new
            {
                t.SlaTimer!.Status,
                t.SlaTimer.StartedAt,
                t.SlaTimer.DueAt
            })
            .ToListAsync(cancellationToken);

        var slaMonitoredCount = monitoredTimers.Count;
        var breachedCount = monitoredTimers.Count(x => x.Status == SlaTimerStatusEnum.Breached);
        var pausedCount = monitoredTimers.Count(x => x.Status == SlaTimerStatusEnum.Paused);
        var nearBreachCount = monitoredTimers.Count(x =>
            x.Status == SlaTimerStatusEnum.Running &&
            TicketQueryHelper.ComputeRemainingPercent(x.Status, x.StartedAt, x.DueAt, now) <= NearBreachThresholdPercent);

        var slaRisk = new SlaRiskDto
        {
            Near = nearBreachCount,
            Breached = breachedCount,
            Healthy = slaMonitoredCount - nearBreachCount - breachedCount
        };

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

        return new CommonResponse<StaffTicketDashboardStatsDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new StaffTicketDashboardStatsDto
            {
                OpenCount = openCount,
                ResolvedCount = resolvedCount,
                NearBreachCount = nearBreachCount,
                BreachedCount = breachedCount,
                PausedCount = pausedCount,
                SlaMonitoredCount = slaMonitoredCount,
                Sla = sla,
                CountByStatus = countByStatus,
                SlaRisk = slaRisk,
                CreatedTrend7Days = createdTrend
            }
        };
    }
}
