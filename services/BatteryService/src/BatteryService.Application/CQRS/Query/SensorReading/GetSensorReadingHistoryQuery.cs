using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.SensorReading;

public class GetSensorReadingHistoryQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<SensorReadingDto>>>
{
    public Guid BatteryAssetId { get; set; }

    public DateTime? From { get; set; }

    public DateTime? To { get; set; }
}
