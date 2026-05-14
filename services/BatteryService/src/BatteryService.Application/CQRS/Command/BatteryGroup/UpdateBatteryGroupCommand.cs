using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.BatteryGroup;

public class UpdateBatteryGroupCommand : CreateBatteryGroupCommand, IRequest<CommonResponse<BatteryGroupDto>>, IValidatable<CommonResponse<BatteryGroupDto>>
{
    public Guid Id { get; set; }

    Task<CommonResponse<BatteryGroupDto>> IValidatable<CommonResponse<BatteryGroupDto>>.ValidateAsync()
    {
        var response = new CommonResponse<BatteryGroupDto>();
        ValidateShared(response);

        if (Id == Guid.Empty)
            AddError(response, nameof(Id), "Id nhóm pin là bắt buộc.");

        return Task.FromResult(response);
    }
}
