using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.BatteryGroup;

public class GetBatteryGroupByIdQuery : IRequest<CommonResponse<BatteryGroupDto>>
{
    public Guid Id { get; set; }
}
