using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Reports;
using TicketService.Application.DTOs.Response.Reports;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Reports;

public class TicketVolumeReportQueryHandler
    : IRequestHandler<TicketVolumeReportQuery, CommonResponse<List<TicketTimeSeriesPoint>>>
{
    private readonly ITicketUnitOfWork _uow;
    public TicketVolumeReportQueryHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<List<TicketTimeSeriesPoint>>> Handle(TicketVolumeReportQuery request, CancellationToken ct)
    {
        var to = request.To ?? DateTime.UtcNow;
        var from = request.From ?? to.AddDays(-30);

        var dates = await _uow.Tickets.GetAllAsync().AsNoTracking()
            .Where(t => !t.IsDeleted && t.CreatedAt >= from && t.CreatedAt <= to)
            .Select(t => t.CreatedAt).ToListAsync(ct);

        var rows = dates
            .GroupBy(d => TicketReportHelpers.Bucket(d, request.Granularity))
            .Select(g => new TicketTimeSeriesPoint { Date = g.Key, Count = g.Count() })
            .OrderBy(p => p.Date).ToList();

        return new CommonResponse<List<TicketTimeSeriesPoint>> { IsSuccess = true, StatusCode = 200, Data = rows };
    }
}
