using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.SensorReading;

public class GetLatestSensorReadingQuery : IRequest<CommonResponse<SensorReadingDto>>
{
    public Guid BatteryAssetId { get; set; }
}
