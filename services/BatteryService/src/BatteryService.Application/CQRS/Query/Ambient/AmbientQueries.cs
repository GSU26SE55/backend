using BatteryService.Application.DTOs;
using MediatR;

namespace BatteryService.Application.CQRS.Query.Ambient;

public class GetAmbientReadingHistoryQuery : IRequest<AmbientReadingListResponse>
{
    /// <summary>ID Site (Guid).</summary>
    public Guid SiteId { get; set; }
    /// <summary>Filter timestamp bắt đầu (UTC inclusive).</summary>
    public DateTime? From { get; set; }
    /// <summary>Filter timestamp kết thúc (UTC inclusive).</summary>
    public DateTime? To { get; set; }
    /// <summary>Số trang (1-based).</summary>
    public int PageNumber { get; set; } = 1;
    /// <summary>Số bản ghi mỗi trang (clamp [1, 100]).</summary>
    public int PageSize { get; set; } = 100;
}

public class GetLatestAmbientReadingQuery : IRequest<AmbientReadingLatestResponse>
{
    /// <summary>ID Site (Guid).</summary>
    public Guid SiteId { get; set; }
}
