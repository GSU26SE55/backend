using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.BatteryAsset;

public class UpdateBatteryAssetCommand : CreateBatteryAssetCommand, IRequest<CommonResponse<BatteryAssetDto>>, IValidatable<CommonResponse<BatteryAssetDto>>
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public WarrantyStatusEnum WarrantyStatus { get; set; } = WarrantyStatusEnum.Active;

    public BatteryStatusEnum Status { get; set; } = BatteryStatusEnum.Active;

    Task<CommonResponse<BatteryAssetDto>> IValidatable<CommonResponse<BatteryAssetDto>>.ValidateAsync()
    {
        var response = new CommonResponse<BatteryAssetDto>();
        ValidateShared(response);

        if (Id == Guid.Empty)
            AddError(response, nameof(Id), "Id tài sản pin là bắt buộc.");

        return Task.FromResult(response);
    }
}
