using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.SensorReading;

public class GetLatestSensorReadingQuery : IRequest<CommonResponse<SensorReadingDto>>
{
    /// <summary>ID BatteryAsset (Guid).</summary>
    public Guid BatteryAssetId { get; set; }
}
