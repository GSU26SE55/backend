using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.ThresholdConfig;

public class UpsertThresholdConfigCommand : IRequest<CommonResponse<ThresholdConfigDto>>, IValidatable<CommonResponse<ThresholdConfigDto>>
{
    /// <summary>ID BatteryType (Guid).</summary>
    public Guid BatteryTypeId { get; set; }

    /// <summary>Điện áp — mốc <b>Warning</b> (V). Vượt mốc này là cảnh báo.</summary>
    public decimal VoltageMin { get; set; }

    /// <summary>Điện áp — mốc <b>Critical</b> (V). Vượt mốc này là nghiêm trọng, đẻ ticket.</summary>
    public decimal VoltageMax { get; set; }

    /// <summary>Nhiệt độ — mốc <b>Critical</b> (°C). Vượt mốc này là nghiêm trọng, đẻ ticket.</summary>
    public decimal TemperatureMax { get; set; }

    /// <summary>Nhiệt độ — mốc <b>Warning</b> (°C). Vượt mốc này là cảnh báo.</summary>
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
            AddError(response, nameof(BatteryTypeId), "Battery type Id is required.");

        if (VoltageMin <= 0)
            AddError(response, nameof(VoltageMin), "Minimum voltage threshold must be greater than 0.");

        if (VoltageMax <= VoltageMin)
            AddCrossFieldError(response, nameof(VoltageMax), "Critical voltage threshold must be greater than the warning threshold.");

        if (TemperatureMax <= TemperatureMin)
            AddCrossFieldError(response, nameof(TemperatureMax), "Critical temperature must be greater than the warning temperature.");

        if (SocWarningThreshold is < 0 or > 100)
            AddError(response, nameof(SocWarningThreshold), "SOC warning threshold must be between 0-100.");

        if (SocCriticalThreshold is < 0 or > 100)
            AddError(response, nameof(SocCriticalThreshold), "SOC critical threshold must be between 0-100.");

        if (SocCriticalThreshold >= SocWarningThreshold)
            AddCrossFieldError(response, nameof(SocCriticalThreshold), "SOC critical threshold must be lower than the warning threshold.");

        if (CurrentMaxCharge.HasValue && CurrentMaxCharge <= 0)
            AddError(response, nameof(CurrentMaxCharge), "Maximum charge current must be greater than 0.");

        if (CurrentMaxDischarge.HasValue && CurrentMaxDischarge <= 0)
            AddError(response, nameof(CurrentMaxDischarge), "Maximum discharge current must be greater than 0.");

        if (SohWarningThreshold is < 0 or > 100)
            AddError(response, nameof(SohWarningThreshold), "SOH warning threshold must be between 0-100.");

        if (SohCriticalThreshold is < 0 or > 100)
            AddError(response, nameof(SohCriticalThreshold), "SOH critical threshold must be between 0-100.");

        if (SohWarningThreshold.HasValue && SohCriticalThreshold.HasValue
            && SohCriticalThreshold.Value >= SohWarningThreshold.Value)
            AddCrossFieldError(response, nameof(SohCriticalThreshold), "SOH critical threshold must be lower than the warning threshold.");

        return Task.FromResult(response);
    }

    private static void AddError(CommonResponse<ThresholdConfigDto> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Invalid battery threshold data.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }

    private static void AddCrossFieldError(CommonResponse<ThresholdConfigDto> response, string field, string detail)
    {
        response.IsSuccess = false;
        // Cross-field business rule violation → 422.
        // Do not overwrite 400 (field-level format errors take precedence).
        if (response.StatusCode != 400)
            response.StatusCode = 422;
        response.Message = "Invalid battery threshold data.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}
