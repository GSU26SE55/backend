using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Command.BatteryAsset;

public class SetBmsSwitchCommand : IRequest<CommonResponse<BmsSwitchCommandAcceptedDto>>
{
    public Guid BatteryAssetId { get; set; }
    public string Target { get; set; } = string.Empty;
    public bool Enable { get; set; }

    /// <summary>
    /// Set khi lệnh do hệ thống phát (consumer sự cố), KHÔNG phải người dùng REST: handler bỏ qua
    /// current-user + tenant scope và ghi thẳng giá trị này vào audit. <c>null</c> = đường REST.
    /// </summary>
    public Guid? IssuedByAccountId { get; set; }
}
