using BatteryService.Application.DTOs;
using MediatR;

namespace BatteryService.Application.CQRS.Query.Ambient;

public class GetAmbientReadingHistoryQuery : IRequest<AmbientReadingListResponse>
{
    public Guid SiteId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}

public class GetLatestAmbientReadingQuery : IRequest<AmbientReadingLatestResponse>
{
    public Guid SiteId { get; set; }
}
