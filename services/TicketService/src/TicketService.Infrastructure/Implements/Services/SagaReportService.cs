using Microsoft.EntityFrameworkCore;
using TicketService.Application.DTOs.Response.Reports;
using TicketService.Application.Interfaces.Services;
using TicketService.Infrastructure.Persistence;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Sprint 7 #114 (§5.2) — đọc <c>AlertTicketSagaState</c> tính saga failed rate theo thời gian.
/// </summary>
public class SagaReportService : ISagaReportService
{
    private const string CompletedState = "Completed";
    private const string FailedState = "Failed";

    private readonly TicketDbContext _dbContext;

    public SagaReportService(TicketDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<SagaFailedRatePoint>> GetFailedRateAsync(
        DateTime? from, DateTime? to, string granularity, CancellationToken cancellationToken = default)
    {
        var toUtc = to ?? DateTime.UtcNow;
        var fromUtc = from ?? toUtc.AddDays(-30);

        var sagas = await _dbContext.AlertTicketSagaStates
            .AsNoTracking()
            .Where(s => s.StartedAt >= fromUtc && s.StartedAt <= toUtc)
            .Select(s => new
            {
                s.StartedAt,
                s.CompletedAt,
                s.FailedAt,
                s.CurrentState
            })
            .ToListAsync(cancellationToken);

        var points = sagas
            .GroupBy(s => Bucket(s.StartedAt, granularity))
            .Select(g =>
            {
                var started = g.Count();
                var completed = g.Count(s => s.CurrentState == CompletedState || s.CompletedAt != null);
                var failed = g.Count(s => s.CurrentState == FailedState || s.FailedAt != null);

                var durations = g
                    .Where(s => s.CompletedAt != null)
                    .Select(s => (s.CompletedAt!.Value - s.StartedAt).TotalSeconds)
                    .Where(d => d >= 0)
                    .OrderBy(d => d)
                    .ToList();

                return new SagaFailedRatePoint
                {
                    Date = g.Key,
                    Started = started,
                    Completed = completed,
                    Failed = failed,
                    FailedRate = started == 0 ? 0m : Math.Round(failed * 100m / started, 2),
                    P95DurationSec = Percentile95(durations)
                };
            })
            .OrderBy(p => p.Date)
            .ToList();

        return points;
    }

    private static DateTime Bucket(DateTime dt, string granularity) => granularity?.ToLowerInvariant() switch
    {
        "month" => new DateTime(dt.Year, dt.Month, 1, 0, 0, 0, DateTimeKind.Utc),
        "week" => DateTime.SpecifyKind(dt.Date.AddDays(-(int)dt.DayOfWeek), DateTimeKind.Utc),
        _ => DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc)
    };

    private static decimal Percentile95(IReadOnlyList<double> sorted)
    {
        if (sorted.Count == 0)
            return 0m;
        // nearest-rank: index = ceil(0.95 * N) - 1
        var rank = (int)Math.Ceiling(0.95 * sorted.Count) - 1;
        rank = Math.Clamp(rank, 0, sorted.Count - 1);
        return Math.Round((decimal)sorted[rank], 2);
    }
}
