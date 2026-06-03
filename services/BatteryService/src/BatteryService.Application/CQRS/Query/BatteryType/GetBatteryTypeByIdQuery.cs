using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.BatteryType;

public class GetBatteryTypeByIdQuery : IRequest<CommonResponse<BatteryTypeDto>>
{
    public Guid Id { get; set; }
}
