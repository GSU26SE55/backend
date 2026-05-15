using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.ThresholdConfig;

public class UpsertThresholdConfigCommand : IRequest<CommonResponse<ThresholdConfigDto>>, IValidatable<CommonResponse<ThresholdConfigDto>>
{
    public Guid BatteryTypeId { get; set; }

    public decimal VoltageMin { get; set; }

    public decimal VoltageMax { get; set; }

    public decimal TemperatureMax { get; set; }

    public decimal TemperatureMin { get; set; }

    public decimal SocWarningThreshold { get; set; }

    public decimal SocCriticalThreshold { get; set; }

    public decimal? CurrentMaxCharge { get; set; }

    public decimal? CurrentMaxDischarge { get; set; }

    public decimal? SohWarningThreshold { get; set; }

    public decimal? SohCriticalThreshold { get; set; }

    public DateTime EffectiveFromUtc { get; set; }

    public Task<CommonResponse<ThresholdConfigDto>> ValidateAsync()
    {
        var response = new CommonResponse<ThresholdConfigDto>();

        if (BatteryTypeId == Guid.Empty)
            AddError(response, nameof(BatteryTypeId), "Id loại pin là bắt buộc.");

        if (VoltageMin <= 0)
            AddError(response, nameof(VoltageMin), "Ngưỡng điện áp tối thiểu phải lớn hơn 0.");

        if (VoltageMax <= VoltageMin)
            AddError(response, nameof(VoltageMax), "Ngưỡng điện áp tối đa phải lớn hơn ngưỡng tối thiểu.");

        if (TemperatureMax <= TemperatureMin)
            AddError(response, nameof(TemperatureMax), "Nhiệt độ tối đa phải lớn hơn nhiệt độ tối thiểu.");

        if (SocWarningThreshold is < 0 or > 100)
            AddError(response, nameof(SocWarningThreshold), "Ngưỡng SOC cảnh báo phải nằm trong khoảng 0-100.");

        if (SocCriticalThreshold is < 0 or > 100)
            AddError(response, nameof(SocCriticalThreshold), "Ngưỡng SOC nghiêm trọng phải nằm trong khoảng 0-100.");

        if (SocCriticalThreshold >= SocWarningThreshold)
            AddError(response, nameof(SocCriticalThreshold), "Ngưỡng SOC nghiêm trọng phải nhỏ hơn ngưỡng cảnh báo.");

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
            AddError(response, nameof(SohCriticalThreshold), "Ngưỡng SOH nghiêm trọng phải nhỏ hơn ngưỡng cảnh báo.");

        return Task.FromResult(response);
    }

    private static void AddError(CommonResponse<ThresholdConfigDto> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Dữ liệu ngưỡng pin không hợp lệ.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}
