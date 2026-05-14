using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.BatteryGroup;

public class CreateBatteryGroupCommand : IRequest<CommonResponse<BatteryGroupDto>>, IValidatable<CommonResponse<BatteryGroupDto>>
{
    public Guid SiteId { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid BatteryTypeId { get; set; }

    public Task<CommonResponse<BatteryGroupDto>> ValidateAsync()
    {
        var response = new CommonResponse<BatteryGroupDto>();
        ValidateShared(response);
        return Task.FromResult(response);
    }

    protected void ValidateShared(CommonResponse<BatteryGroupDto> response)
    {
        if (SiteId == Guid.Empty)
            AddError(response, nameof(SiteId), "Id site là bắt buộc.");

        if (string.IsNullOrWhiteSpace(Name))
            AddError(response, nameof(Name), "Tên nhóm pin là bắt buộc.");
        else if (Name.Trim().Length > 100)
            AddError(response, nameof(Name), "Tên nhóm pin tối đa 100 ký tự.");

        if (BatteryTypeId == Guid.Empty)
            AddError(response, nameof(BatteryTypeId), "Id loại pin là bắt buộc.");
    }

    protected static void AddError(CommonResponse<BatteryGroupDto> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Dữ liệu nhóm pin không hợp lệ.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}
