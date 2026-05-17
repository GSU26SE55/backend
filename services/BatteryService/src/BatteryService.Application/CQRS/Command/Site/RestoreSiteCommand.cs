using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Command.Site;

public class RestoreSiteCommand : IRequest<CommonResponse<object>>
{
    public Guid Id { get; set; }
}
