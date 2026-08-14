using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Command.BatteryAsset;

public class SetBmsSwitchCommand : IRequest<CommonResponse<BmsSwitchCommandAcceptedDto>>
{
    public Guid BatteryAssetId { get; set; }
    public string Target { get; set; } = string.Empty;
    public bool Enable { get; set; }
}
