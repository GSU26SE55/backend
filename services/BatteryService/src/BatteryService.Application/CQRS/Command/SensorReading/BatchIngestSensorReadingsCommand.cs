using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.SensorReading;

public class BatchIngestSensorReadingsCommand : IRequest<CommonResponse<SensorReadingBatchIngestResult>>, IValidatable<CommonResponse<SensorReadingBatchIngestResult>>
{
    /// <summary>Danh sách items.</summary>
    public List<SensorReadingItem> Items { get; set; } = new();

    /// <summary>Sprint IoT-1 (#246) — header <c>X-Device-Code</c>. Backend cross-check với device được auth.</summary>
    [JsonIgnore]
    [BindNever]
    public string? DeviceCode { get; set; }

    /// <summary>Sprint IoT-1 (#246) — header <c>Idempotency-Key</c>. Retry an toàn cùng key trả response cũ.</summary>
    [JsonIgnore]
    [BindNever]
    public string? IdempotencyKey { get; set; }

    /// <summary>Sprint IoT-1 (#246) — DeviceId resolve từ X-Api-Key (per-device). Null nếu dùng legacy global key.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid? AuthenticatedDeviceId { get; set; }

    public Task<CommonResponse<SensorReadingBatchIngestResult>> ValidateAsync()
    {
        var response = new CommonResponse<SensorReadingBatchIngestResult>();

        if (Items.Count == 0)
            AddError(response, nameof(Items), "Reading list is required.");

        if (Items.Count > 1000)
            AddError(response, nameof(Items), "Batch must not exceed 1000 readings.");

        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            var prefix = $"{nameof(Items)}[{i}]";

            if (item.BatteryAssetId == Guid.Empty && string.IsNullOrWhiteSpace(item.BatteryAssetSerial))
                AddError(response, $"{prefix}.{nameof(item.BatteryAssetId)}", "BatteryAssetId or BatteryAssetSerial is required.");

            if (item.Time == default)
                AddError(response, $"{prefix}.{nameof(item.Time)}", "Reading timestamp is required.");
            else if (item.Time.ToUniversalTime() > DateTime.UtcNow.AddMinutes(5))
                AddError(response, $"{prefix}.{nameof(item.Time)}", "Reading timestamp cannot be too far in the future.");

            // Clock skew check ĐÃ MOVE vào handler để fire metric reason=clock_drift (#IoT2-15).

            if (item.BatteryAssetSerial?.Length > 64)
                AddError(response, $"{prefix}.{nameof(item.BatteryAssetSerial)}", "BatteryAssetSerial must not exceed 64 characters.");

            // Sprint IoT-2 #IoT2-15/#IoT2-17 — sanity check ONLY (cực biên hardware noise).
            // Outlier reject + clock-drift kiểm tra trong HANDLER để metric counters fire đúng (§52.5).
            if (item.Voltage < 0)
                AddError(response, $"{prefix}.{nameof(item.Voltage)}", "Voltage must not be negative.");

            if (item.CycleCount is < 0)
                AddError(response, $"{prefix}.{nameof(item.CycleCount)}", "Cycle count must not be negative.");

            if (item.SourceDeviceId?.Length > 64)
                AddError(response, $"{prefix}.{nameof(item.SourceDeviceId)}", "Source device Id must not exceed 64 characters.");

            if (item.ChargingState.HasValue && !Enum.IsDefined(typeof(ChargingStateEnum), item.ChargingState.Value))
                AddError(response, $"{prefix}.{nameof(item.ChargingState)}", "Invalid ChargingState.");

            // Sprint 5B #105 — Tier 2 validation.
            if (item.InternalResistanceMilliohm is <= 0)
                AddError(response, $"{prefix}.{nameof(item.InternalResistanceMilliohm)}", "Internal resistance must be > 0 mΩ.");

            if (item.CellVoltageDeltaMv is < 0)
                AddError(response, $"{prefix}.{nameof(item.CellVoltageDeltaMv)}", "Cell voltage delta must not be negative.");

            // Sprint 5B B9 — SourceType + length validation.
            if (!Enum.IsDefined(typeof(SensorReadingSourceTypeEnum), item.SourceType))
                AddError(response, $"{prefix}.{nameof(item.SourceType)}", "Invalid SourceType.");

            if (item.BmsErrorCode?.Length > 64)
                AddError(response, $"{prefix}.{nameof(item.BmsErrorCode)}", "BmsErrorCode must not exceed 64 characters.");

            if (item.SensorSourceCode?.Length > 20)
                AddError(response, $"{prefix}.{nameof(item.SensorSourceCode)}", "SensorSourceCode must not exceed 20 characters.");
        }

        return Task.FromResult(response);
    }

    private static void AddError(CommonResponse<SensorReadingBatchIngestResult> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Invalid sensor reading data.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}

public class SensorReadingItem
{
    /// <summary>Timestamp của reading (UTC).</summary>
    public DateTime Time { get; set; }

    /// <summary>
    /// Sprint IoT-1 (#246) — timestamp ghi nhận tại device. Có thể khác Time (Time = backend persist).
    /// Backend dùng để tính clock skew + validate &lt;= 5 phút (§247).
    /// </summary>
    public DateTime? DeviceTimestamp { get; set; }

    /// <summary>
    /// Sprint IoT-1 (#246) — Serial của BatteryAsset (vd "BAT-001"). Mapping → BatteryAssetId tại backend.
    /// Ưu tiên field này; nếu null backend dùng <see cref="BatteryAssetId"/> (legacy/simulator).
    /// </summary>
    public string? BatteryAssetSerial { get; set; }

    /// <summary>ID BatteryAsset (Guid).</summary>
    public Guid BatteryAssetId { get; set; }

    /// <summary>Điện áp (V).</summary>
    public decimal Voltage { get; set; }

    /// <summary>Cường độ dòng (A). Âm = xả, dương = sạc.</summary>
    public decimal Current { get; set; }

    /// <summary>Nhiệt độ (°C).</summary>
    public decimal Temperature { get; set; }

    /// <summary>State of Charge — % pin còn (0..100).</summary>
    public decimal SocPercent { get; set; }

    /// <summary>Số chu kỳ sạc/xả của pin.</summary>
    public int? CycleCount { get; set; }

    /// <summary>State of Health — % sức khoẻ pin (0..100).</summary>
    public decimal? SohPercent { get; set; }

    /// <summary>Trạng thái sạc (Idle / Charging / Discharging / Full).</summary>
    public ChargingStateEnum? ChargingState { get; set; }

    /// <summary>ID thiết bị nguồn (≤ 64 ký tự).</summary>
    public string? SourceDeviceId { get; set; }

    // Sprint 5B #101/#105 — Tier 2 battery health metrics.
    /// <summary>Điện trở nội (mΩ) — tier 2 health metric.</summary>
    public decimal? InternalResistanceMilliohm { get; set; }
    /// <summary>Chênh điện áp giữa cell max và cell min (mV).</summary>
    public decimal? CellVoltageDeltaMv { get; set; }

    // Sprint 5B B9 (#154) — phân biệt nguồn đo (BMS vs IoT vs External).
    /// <summary>Phân loại nguồn (Bms | IotGateway | External).</summary>
    public SensorReadingSourceTypeEnum SourceType { get; set; } = SensorReadingSourceTypeEnum.IotGateway;

    /// <summary>BMS error raw code (vd "0x0A", "OverCurrent,CellImbalance"). Tối đa 64 ký tự.</summary>
    public string? BmsErrorCode { get; set; }

    /// <summary>§52.9 — "primary"/"redundant"/"external-temp". Tối đa 20 ký tự.</summary>
    public string? SensorSourceCode { get; set; }
}
