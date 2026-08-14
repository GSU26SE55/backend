using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.BatteryAsset;

public class UpdateBatteryAssetCommand : CreateBatteryAssetCommand, IRequest<CommonResponse<BatteryAssetDto>>, IValidatable<CommonResponse<BatteryAssetDto>>
{
    /// <summary>Định danh resource.</summary>
    [JsonIgnore]
    public Guid Id { get; set; }

    /// <summary>Trạng thái bảo hành (Active | Expired).</summary>
    public WarrantyStatusEnum WarrantyStatus { get; set; } = WarrantyStatusEnum.Active;

    /// <summary>Filter theo status enum.</summary>
    public BatteryStatusEnum Status { get; set; } = BatteryStatusEnum.Active;

    Task<CommonResponse<BatteryAssetDto>> IValidatable<CommonResponse<BatteryAssetDto>>.ValidateAsync()
    {
        var response = new CommonResponse<BatteryAssetDto>();
        ValidateShared(response);

        if (Id == Guid.Empty)
            AddError(response, nameof(Id), "Battery asset Id is required.");

        return Task.FromResult(response);
    }
}
