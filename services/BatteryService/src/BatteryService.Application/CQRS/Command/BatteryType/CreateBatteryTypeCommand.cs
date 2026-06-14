using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.BatteryType;

public class CreateBatteryTypeCommand : IRequest<CommonResponse<BatteryTypeDto>>, IValidatable<CommonResponse<BatteryTypeDto>>
{
    /// <summary>Tên hiển thị.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Tên nhà sản xuất.</summary>
    public string? Manufacturer { get; set; }

    /// <summary>Dung lượng danh nghĩa (Ah).</summary>
    public decimal NominalCapacityAh { get; set; }

    /// <summary>Điện áp danh nghĩa (V).</summary>
    public decimal NominalVoltage { get; set; }

    /// <summary>Hoá học pin (LiFePO4 | NMC | NCA).</summary>
    public BatteryChemistryEnum Chemistry { get; set; } = BatteryChemistryEnum.LiFePO4;

    /// <summary>Tối đa số chu kỳ trước EOL.</summary>
    public int MaxCycleCount { get; set; } = 2000;

    /// <summary>Mô tả chi tiết.</summary>
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
