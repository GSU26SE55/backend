using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Reports;
using TicketService.Application.DTOs.Response.Reports;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Reports;

public class ResolutionTimeHistogramReportQueryHandler
    : IRequestHandler<ResolutionTimeHistogramReportQuery, CommonResponse<List<HistogramBucketRow>>>
{
    private readonly ITicketUnitOfWork _uow;
    public ResolutionTimeHistogramReportQueryHandler(ITicketUnitOfWork uow) => _uow = uow;

    private static readonly (string Label, double MaxHours)[] Buckets =
    {
        ("0-1h", 1), ("1-4h", 4), ("4-24h", 24), ("1-3d", 72), (">3d", double.MaxValue)
    };

    public async Task<CommonResponse<List<HistogramBucketRow>>> Handle(ResolutionTimeHistogramReportQuery request, CancellationToken ct)
    {
        var q = _uow.Tickets.GetAllAsync().AsNoTracking().Where(t => !t.IsDeleted && t.ResolvedAt != null);
        if (request.From.HasValue) q = q.Where(t => t.CreatedAt >= request.From.Value);
        if (request.To.HasValue) q = q.Where(t => t.CreatedAt <= request.To.Value);

        var data = await q.Select(t => new { t.CreatedAt, t.ResolvedAt }).ToListAsync(ct);

        var counts = Buckets.ToDictionary(b => b.Label, _ => 0);
        foreach (var t in data)
        {
            var hours = (t.ResolvedAt!.Value - t.CreatedAt).TotalHours;
            if (hours < 0) hours = 0;
            var bucket = Buckets.First(b => hours <= b.MaxHours);
            counts[bucket.Label]++;
        }

        var rows = Buckets.Select(b => new HistogramBucketRow { Bucket = b.Label, Count = counts[b.Label] }).ToList();
        return new CommonResponse<List<HistogramBucketRow>> { IsSuccess = true, StatusCode = 200, Data = rows };
    }
}
