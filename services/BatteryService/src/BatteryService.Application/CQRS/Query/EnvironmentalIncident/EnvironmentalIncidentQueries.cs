using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;

namespace BatteryService.Application.CQRS.Query.EnvironmentalIncident;

public class GetEnvironmentalIncidentsQuery : IRequest<EnvironmentalIncidentListResponse>
{
    public Guid? SiteId { get; set; }
    public EnvironmentalIncidentStatusEnum? Status { get; set; }
    public EnvironmentalIncidentTypeEnum? IncidentType { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class GetEnvironmentalIncidentByIdQuery : IRequest<EnvironmentalIncidentResponse>
{
    public Guid Id { get; set; }
}

public class ActiveEnvironmentalIncidentsBySiteQuery : IRequest<EnvironmentalIncidentListResponse>
{
    public Guid SiteId { get; set; }
}
