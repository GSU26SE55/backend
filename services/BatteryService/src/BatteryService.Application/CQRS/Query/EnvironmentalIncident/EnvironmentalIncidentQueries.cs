using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;

namespace BatteryService.Application.CQRS.Query.EnvironmentalIncident;

public class GetEnvironmentalIncidentsQuery : IRequest<EnvironmentalIncidentListResponse>
{
    /// <summary>ID Site (Guid).</summary>
    public Guid? SiteId { get; set; }
    /// <summary>Filter theo status enum.</summary>
    public EnvironmentalIncidentStatusEnum? Status { get; set; }
    /// <summary>Loại incident (SmokeDetected | WaterLeak | ...).</summary>
    public EnvironmentalIncidentTypeEnum? IncidentType { get; set; }
    /// <summary>Filter timestamp bắt đầu (UTC inclusive).</summary>
    public DateTime? From { get; set; }
    /// <summary>Filter timestamp kết thúc (UTC inclusive).</summary>
    public DateTime? To { get; set; }
    /// <summary>Số trang (1-based).</summary>
    public int PageNumber { get; set; } = 1;
    /// <summary>Số bản ghi mỗi trang (clamp [1, 100]).</summary>
    public int PageSize { get; set; } = 50;
}

public class GetEnvironmentalIncidentByIdQuery : IRequest<EnvironmentalIncidentResponse>
{
    /// <summary>Định danh resource.</summary>
    public Guid Id { get; set; }
}

public class ActiveEnvironmentalIncidentsBySiteQuery : IRequest<EnvironmentalIncidentListResponse>
{
    /// <summary>ID Site (Guid).</summary>
    public Guid SiteId { get; set; }
}
