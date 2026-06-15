using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.ThresholdConfig;

public class UpsertThresholdConfigCommand : IRequest<CommonResponse<ThresholdConfigDto>>, IValidatable<CommonResponse<ThresholdConfigDto>>
{
    /// <summary>ID BatteryType (Guid).</summary>
    public Guid BatteryTypeId { get; set; }

    /// <summary>Điện áp tối thiểu cho phép (V).</summary>
    public decimal VoltageMin { get; set; }

    /// <summary>Điện áp tối đa cho phép (V).</summary>
    public decimal VoltageMax { get; set; }

    /// <summary>Nhiệt độ tối đa (°C).</summary>
    public decimal TemperatureMax { get; set; }

    /// <summary>Nhiệt độ tối thiểu (°C).</summary>
    public decimal TemperatureMin { get; set; }

    /// <summary>SOC threshold Warning (vd 20%).</summary>
    public decimal SocWarningThreshold { get; set; }

    /// <summary>SOC threshold Critical (vd 10%).</summary>
    public decimal SocCriticalThreshold { get; set; }

    /// <summary>Dòng sạc tối đa (A).</summary>
    public decimal? CurrentMaxCharge { get; set; }

    /// <summary>Dòng xả tối đa (A).</summary>
    public decimal? CurrentMaxDischarge { get; set; }

    /// <summary>SOH threshold Warning (vd 85%).</summary>
    public decimal? SohWarningThreshold { get; set; }

    /// <summary>SOH threshold Critical (vd 75%).</summary>
    public decimal? SohCriticalThreshold { get; set; }

    /// <summary>Field EffectiveFromUtc.</summary>
    public DateTime EffectiveFromUtc { get; set; }

    public Task<CommonResponse<ThresholdConfigDto>> ValidateAsync()
    {
        var response = new CommonResponse<ThresholdConfigDto>();

        if (BatteryTypeId == Guid.Empty)
            AddError(response, nameof(BatteryTypeId), "Id loại pin là bắt buộc.");

        if (VoltageMin <= 0)
            AddError(response, nameof(VoltageMin), "Ngưỡng điện áp tối thiểu phải lớn hơn 0.");

        if (VoltageMax <= VoltageMin)
            AddCrossFieldError(response, nameof(VoltageMax), "Ngưỡng điện áp tối đa phải lớn hơn ngưỡng tối thiểu.");

        if (TemperatureMax <= TemperatureMin)
            AddCrossFieldError(response, nameof(TemperatureMax), "Nhiệt độ tối đa phải lớn hơn nhiệt độ tối thiểu.");

        if (SocWarningThreshold is < 0 or > 100)
            AddError(response, nameof(SocWarningThreshold), "Ngưỡng SOC cảnh báo phải nằm trong khoảng 0-100.");

        if (SocCriticalThreshold is < 0 or > 100)
            AddError(response, nameof(SocCriticalThreshold), "Ngưỡng SOC nghiêm trọng phải nằm trong khoảng 0-100.");

        if (SocCriticalThreshold >= SocWarningThreshold)
            AddCrossFieldError(response, nameof(SocCriticalThreshold), "Ngưỡng SOC nghiêm trọng phải nhỏ hơn ngưỡng cảnh báo.");

        if (CurrentMaxCharge.HasValue && CurrentMaxCharge <= 0)
            AddError(response, nameof(CurrentMaxCharge), "Dòng sạc tối đa phải lớn hơn 0.");

        if (CurrentMaxDischarge.HasValue && CurrentMaxDischarge <= 0)
            AddError(response, nameof(CurrentMaxDischarge), "Dòng xả tối đa phải lớn hơn 0.");

        if (SohWarningThreshold is < 0 or > 100)
            AddError(response, nameof(SohWarningThreshold), "Ngưỡng SOH cảnh báo phải nằm trong khoảng 0-100.");

        if (SohCriticalThreshold is < 0 or > 100)
            AddError(response, nameof(SohCriticalThreshold), "Ngưỡng SOH nghiêm trọng phải nằm trong khoảng 0-100.");

        if (SohWarningThreshold.HasValue && SohCriticalThreshold.HasValue
            && SohCriticalThreshold.Value >= SohWarningThreshold.Value)
            AddCrossFieldError(response, nameof(SohCriticalThreshold), "Ngưỡng SOH nghiêm trọng phải nhỏ hơn ngưỡng cảnh báo.");

        return Task.FromResult(response);
    }

    private static void AddError(CommonResponse<ThresholdConfigDto> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Dữ liệu ngưỡng pin không hợp lệ.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }

    private static void AddCrossFieldError(CommonResponse<ThresholdConfigDto> response, string field, string detail)
    {
        response.IsSuccess = false;
        // Cross-field business rule violation → 422.
        // Do not overwrite 400 (field-level format errors take precedence).
        if (response.StatusCode != 400)
            response.StatusCode = 422;
        response.Message = "Dữ liệu ngưỡng pin không hợp lệ.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}
