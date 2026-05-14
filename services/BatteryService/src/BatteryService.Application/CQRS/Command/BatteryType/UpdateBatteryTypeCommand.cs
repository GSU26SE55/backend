using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.BatteryType;

public class UpdateBatteryTypeCommand : IRequest<CommonResponse<BatteryTypeDto>>, IValidatable<CommonResponse<BatteryTypeDto>>
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Manufacturer { get; set; }

    public decimal NominalCapacityAh { get; set; }

    public decimal NominalVoltage { get; set; }

    public BatteryChemistryEnum Chemistry { get; set; } = BatteryChemistryEnum.LiFePO4;

    public int MaxCycleCount { get; set; } = 2000;

    public string? Description { get; set; }

    public Task<CommonResponse<BatteryTypeDto>> ValidateAsync()
    {
        var response = new CommonResponse<BatteryTypeDto>();

        if (string.IsNullOrWhiteSpace(Name))
            AddError(response, nameof(Name), "Tên loại pin là bắt buộc.");
        else if (Name.Trim().Length > 100)
            AddError(response, nameof(Name), "Tên loại pin tối đa 100 ký tự.");

        if (Manufacturer?.Length > 100)
            AddError(response, nameof(Manufacturer), "Nhà sản xuất tối đa 100 ký tự.");

        if (NominalCapacityAh <= 0)
            AddError(response, nameof(NominalCapacityAh), "Dung lượng danh định phải lớn hơn 0.");

        if (NominalVoltage <= 0)
            AddError(response, nameof(NominalVoltage), "Điện áp danh định phải lớn hơn 0.");

        if (MaxCycleCount <= 0)
            AddError(response, nameof(MaxCycleCount), "Số chu kỳ tối đa phải lớn hơn 0.");

        if (Description?.Length > 500)
            AddError(response, nameof(Description), "Mô tả tối đa 500 ký tự.");

        if (Id == Guid.Empty)
            AddError(response, nameof(Id), "Id loại pin là bắt buộc.");

        return Task.FromResult(response);
    }

    private static void AddError(CommonResponse<BatteryTypeDto> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Dữ liệu loại pin không hợp lệ.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}
