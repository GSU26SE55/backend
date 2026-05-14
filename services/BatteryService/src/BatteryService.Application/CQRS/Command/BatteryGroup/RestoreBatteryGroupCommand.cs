using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Command.BatteryGroup;

public class RestoreBatteryGroupCommand : IRequest<CommonResponse<object>>
{
    public Guid Id { get; set; }
}
