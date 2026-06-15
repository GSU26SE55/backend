using BatteryService.Application.CQRS.Query.Ambient;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Ambient;

public class GetAmbientReadingHistoryQueryHandler
    : IRequestHandler<GetAmbientReadingHistoryQuery, AmbientReadingListResponse>
{
    private readonly IBatteryUnitOfWork _uow;
    public GetAmbientReadingHistoryQueryHandler(IBatteryUnitOfWork uow) { _uow = uow; }

    public async Task<AmbientReadingListResponse> Handle(GetAmbientReadingHistoryQuery request, CancellationToken cancellationToken)
    {
        var pageSize = request.PageSize is < 1 or > 1000 ? 100 : request.PageSize;
        var pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;

        var query = _uow.AmbientReadings.GetAllAsync().Where(r => r.SiteId == request.SiteId);
        if (request.From.HasValue)
            query = query.Where(r => r.Time >= request.From.Value);
        if (request.To.HasValue)
            query = query.Where(r => r.Time <= request.To.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.Time)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new AmbientReadingDto
            {
                Time = r.Time,
                SiteId = r.SiteId.ToString(),
                AmbientTemperature = r.AmbientTemperature,
                Humidity = r.Humidity,
                SolarIrradiance = r.SolarIrradiance,
                Source = r.Source,
                SourceDeviceId = r.SourceDeviceId
            })
            .ToListAsync(cancellationToken);

        return new AmbientReadingListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<AmbientReadingDto>
            {
                Items = items,
                TotalItems = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            }
        };
    }
}

public class GetLatestAmbientReadingQueryHandler
    : IRequestHandler<GetLatestAmbientReadingQuery, AmbientReadingLatestResponse>
{
    private readonly IBatteryUnitOfWork _uow;
    public GetLatestAmbientReadingQueryHandler(IBatteryUnitOfWork uow) { _uow = uow; }

    public async Task<AmbientReadingLatestResponse> Handle(GetLatestAmbientReadingQuery request, CancellationToken cancellationToken)
    {
        var row = await _uow.AmbientReadings.GetAllAsync()
            .Where(r => r.SiteId == request.SiteId)
            .OrderByDescending(r => r.Time)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return new AmbientReadingLatestResponse { IsSuccess = false, StatusCode = 404, Message = "No reading found." };

        return new AmbientReadingLatestResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new AmbientReadingDto
            {
                Time = row.Time,
                SiteId = row.SiteId.ToString(),
                AmbientTemperature = row.AmbientTemperature,
                Humidity = row.Humidity,
                SolarIrradiance = row.SolarIrradiance,
                Source = row.Source,
                SourceDeviceId = row.SourceDeviceId
            }
        };
    }
}
