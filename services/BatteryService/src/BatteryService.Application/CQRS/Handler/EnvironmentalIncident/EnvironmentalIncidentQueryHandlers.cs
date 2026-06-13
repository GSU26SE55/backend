using BatteryService.Application.CQRS.Query.EnvironmentalIncident;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.EnvironmentalIncident;

public class GetEnvironmentalIncidentsQueryHandler
    : IRequestHandler<GetEnvironmentalIncidentsQuery, EnvironmentalIncidentListResponse>
{
    private readonly IBatteryUnitOfWork _uow;
    public GetEnvironmentalIncidentsQueryHandler(IBatteryUnitOfWork uow) { _uow = uow; }

    public async Task<EnvironmentalIncidentListResponse> Handle(GetEnvironmentalIncidentsQuery request, CancellationToken cancellationToken)
    {
        var pageSize = request.PageSize is < 1 or > 200 ? 50 : request.PageSize;
        var pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;

        var query = _uow.EnvironmentalIncidents.GetAllAsync().Where(i => !i.IsDeleted);

        if (request.SiteId.HasValue)
            query = query.Where(i => i.SiteId == request.SiteId.Value);
        if (request.Status.HasValue)
            query = query.Where(i => i.Status == request.Status.Value);
        if (request.IncidentType.HasValue)
            query = query.Where(i => i.IncidentType == request.IncidentType.Value);
        if (request.From.HasValue)
            query = query.Where(i => i.DetectedAt >= request.From.Value);
        if (request.To.HasValue)
            query = query.Where(i => i.DetectedAt <= request.To.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(i => i.DetectedAt)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        return new EnvironmentalIncidentListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<EnvironmentalIncidentDto>
            {
                Items = items.Select(ReportEnvironmentalIncidentCommandHandler.Map).ToList(),
                TotalItems = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            }
        };
    }
}

public class GetEnvironmentalIncidentByIdQueryHandler
    : IRequestHandler<GetEnvironmentalIncidentByIdQuery, EnvironmentalIncidentResponse>
{
    private readonly IBatteryUnitOfWork _uow;
    public GetEnvironmentalIncidentByIdQueryHandler(IBatteryUnitOfWork uow) { _uow = uow; }

    public async Task<EnvironmentalIncidentResponse> Handle(GetEnvironmentalIncidentByIdQuery request, CancellationToken cancellationToken)
    {
        var i = await _uow.EnvironmentalIncidents.GetByIdAsync(request.Id);
        if (i is null || i.IsDeleted)
            return new EnvironmentalIncidentResponse { IsSuccess = false, StatusCode = 404, Message = "Incident not found." };

        return new EnvironmentalIncidentResponse { IsSuccess = true, StatusCode = 200, Data = ReportEnvironmentalIncidentCommandHandler.Map(i) };
    }
}

public class ActiveEnvironmentalIncidentsBySiteQueryHandler
    : IRequestHandler<ActiveEnvironmentalIncidentsBySiteQuery, EnvironmentalIncidentListResponse>
{
    private readonly IBatteryUnitOfWork _uow;
    public ActiveEnvironmentalIncidentsBySiteQueryHandler(IBatteryUnitOfWork uow) { _uow = uow; }

    public async Task<EnvironmentalIncidentListResponse> Handle(ActiveEnvironmentalIncidentsBySiteQuery request, CancellationToken cancellationToken)
    {
        var items = await _uow.EnvironmentalIncidents.GetAllAsync()
            .Where(i => !i.IsDeleted
                        && i.SiteId == request.SiteId
                        && (i.Status == EnvironmentalIncidentStatusEnum.Open
                            || i.Status == EnvironmentalIncidentStatusEnum.Acknowledged))
            .OrderByDescending(i => i.DetectedAt)
            .ToListAsync(cancellationToken);

        return new EnvironmentalIncidentListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<EnvironmentalIncidentDto>
            {
                Items = items.Select(ReportEnvironmentalIncidentCommandHandler.Map).ToList(),
                TotalItems = items.Count,
                PageNumber = 1,
                PageSize = items.Count
            }
        };
    }
}
