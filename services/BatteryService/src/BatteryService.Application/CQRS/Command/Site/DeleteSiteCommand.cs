using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Command.Site;

public class DeleteSiteCommand : IRequest<CommonResponse<object>>
{
    public Guid Id { get; set; }
}
