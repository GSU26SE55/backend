using BatteryService.Application.CQRS.Command.Ambient;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Ambient;

public class BatchIngestAmbientReadingsCommandHandler
    : IRequestHandler<BatchIngestAmbientReadingsCommand, CommonResponse<int>>
{
    private readonly IBatteryUnitOfWork _uow;

    public BatchIngestAmbientReadingsCommandHandler(IBatteryUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<int>> Handle(BatchIngestAmbientReadingsCommand request, CancellationToken cancellationToken)
    {
        foreach (var x in request.Items)
        {
            await _uow.AmbientReadings.AddAsync(new AmbientReading
            {
                Time = x.Time.Kind == DateTimeKind.Utc ? x.Time : x.Time.ToUniversalTime(),
                SiteId = x.SiteId,
                AmbientTemperature = x.AmbientTemperature,
                Humidity = x.Humidity,
                SolarIrradiance = x.SolarIrradiance,
                Source = x.Source,
                SourceDeviceId = x.SourceDeviceId
            });
        }

        await _uow.SaveChangesAsync(cancellationToken);

        return new CommonResponse<int>
        {
            IsSuccess = true,
            StatusCode = 201,
            Data = request.Items.Count
        };
    }
}
