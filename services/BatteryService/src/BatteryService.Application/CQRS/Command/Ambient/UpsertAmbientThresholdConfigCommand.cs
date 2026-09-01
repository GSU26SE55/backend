using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.Ambient;

/// <summary>
/// Sprint 5B #92 — upsert AmbientThresholdConfig per site.
/// </summary>
public class UpsertAmbientThresholdConfigCommand
    : IRequest<AmbientThresholdConfigResponse>, IValidatable<AmbientThresholdConfigResponse>
{
    /// <summary>ID Site (Guid).</summary>
    public Guid SiteId { get; set; }
    /// <summary>Ambient temp Warning threshold (°C).</summary>
    public decimal? HighAmbientTempWarning { get; set; }
    /// <summary>Ambient temp Critical threshold (°C).</summary>
    public decimal? HighAmbientTempCritical { get; set; }
    /// <summary>Humidity Warning threshold (%).</summary>
    public decimal? HighHumidityWarning { get; set; }
    /// <summary>Humidity Critical threshold (%).</summary>
    public decimal? HighHumidityCritical { get; set; }
    /// <summary>Gas Concentration Warning threshold (%).</summary>
    public decimal? HighGasWarning { get; set; }
    /// <summary>Gas Concentration Critical threshold (%).</summary>
    public decimal? HighGasCritical { get; set; }
    /// <summary>Combo temp threshold (cùng với humidity).</summary>
    public decimal? ComboTempThreshold { get; set; }
    /// <summary>Combo humidity threshold.</summary>
    public decimal? ComboHumidityThreshold { get; set; }
    /// <summary>Tính năng bật/tắt.</summary>
    public bool Enabled { get; set; } = true;

    // Nhiệt độ đủ rộng cho mọi site thật nhưng vẫn chặn được typo sai bậc; độ ẩm là phần trăm.
    private const decimal TempMin = -50m;
    private const decimal TempMax = 150m;
    private const decimal HumidityMin = 0m;
    private const decimal HumidityMax = 100m;
    private const decimal GasMin = 0m;
    private const decimal GasMax = 100m;

    private static void AddRangeError(
        AmbientThresholdConfigResponse response,
        decimal? value,
        string field,
        decimal min,
        decimal max,
        string unit)
    {
        if (value.HasValue && (value.Value < min || value.Value > max))
        {
            response.ListErrors.Add(new Errors
            {
                Field = field,
                Detail = $"{field} must be between {min} and {max} {unit}."
            });
        }
    }

    public Task<AmbientThresholdConfigResponse> ValidateAsync()
    {
        var response = new AmbientThresholdConfigResponse();

        if (SiteId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = nameof(SiteId), Detail = "SiteId is required." });

        // Khoảng hợp lệ trước đây chỉ FE kiểm. Gọi thẳng API là lưu được "500" vào một field
        // tính bằng %, tạo ra ngưỡng không bao giờ trip được — sai âm thầm, không ai thấy.
        AddRangeError(response, HighAmbientTempWarning, nameof(HighAmbientTempWarning), TempMin, TempMax, "°C");
        AddRangeError(response, HighAmbientTempCritical, nameof(HighAmbientTempCritical), TempMin, TempMax, "°C");
        AddRangeError(response, ComboTempThreshold, nameof(ComboTempThreshold), TempMin, TempMax, "°C");
        AddRangeError(response, HighHumidityWarning, nameof(HighHumidityWarning), HumidityMin, HumidityMax, "%");
        AddRangeError(response, HighHumidityCritical, nameof(HighHumidityCritical), HumidityMin, HumidityMax, "%");
        AddRangeError(response, ComboHumidityThreshold, nameof(ComboHumidityThreshold), HumidityMin, HumidityMax, "%");
        AddRangeError(response, HighGasWarning, nameof(HighGasWarning), GasMin, GasMax, "%");
        AddRangeError(response, HighGasCritical, nameof(HighGasCritical), GasMin, GasMax, "%");

        if (HighAmbientTempWarning.HasValue && HighAmbientTempCritical.HasValue
            && HighAmbientTempCritical.Value < HighAmbientTempWarning.Value)
        {
            response.ListErrors.Add(new Errors
            {
                Field = nameof(HighAmbientTempCritical),
                Detail = "Critical must be >= Warning."
            });
        }

        if (HighHumidityWarning.HasValue && HighHumidityCritical.HasValue
            && HighHumidityCritical.Value < HighHumidityWarning.Value)
        {
            response.ListErrors.Add(new Errors
            {
                Field = nameof(HighHumidityCritical),
                Detail = "Critical must be >= Warning."
            });
        }

        if (HighGasWarning.HasValue && HighGasCritical.HasValue
            && HighGasCritical.Value < HighGasWarning.Value)
        {
            response.ListErrors.Add(new Errors
            {
                Field = nameof(HighGasCritical),
                Detail = "Critical must be >= Warning."
            });
        }

        // Combo rule cần cả hai vế: chỉ set một nửa thì rule không bao giờ chạy, cấu hình
        // trông như đã bật nhưng thực tế là chết. FE đã chặn, BE trước đây thì không.
        if (ComboTempThreshold.HasValue != ComboHumidityThreshold.HasValue)
        {
            response.ListErrors.Add(new Errors
            {
                Field = ComboTempThreshold.HasValue
                    ? nameof(ComboHumidityThreshold)
                    : nameof(ComboTempThreshold),
                Detail = "Combo rule needs both temperature and humidity thresholds."
            });
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid threshold config.";
        }
        return Task.FromResult(response);
    }
}
