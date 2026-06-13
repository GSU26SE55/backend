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
            AddError(response, nameof(Items), "Danh sách readings là bắt buộc.");

        if (Items.Count > 1000)
            AddError(response, nameof(Items), "Batch không được vượt quá 1000 readings.");

        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            var prefix = $"{nameof(Items)}[{i}]";

            if (item.BatteryAssetId == Guid.Empty && string.IsNullOrWhiteSpace(item.BatteryAssetSerial))
                AddError(response, $"{prefix}.{nameof(item.BatteryAssetId)}", "Phải có BatteryAssetId hoặc BatteryAssetSerial.");

            if (item.Time == default)
                AddError(response, $"{prefix}.{nameof(item.Time)}", "Thời điểm reading là bắt buộc.");
            else if (item.Time.ToUniversalTime() > DateTime.UtcNow.AddMinutes(5))
                AddError(response, $"{prefix}.{nameof(item.Time)}", "Thời điểm reading không được nằm quá xa trong tương lai.");

            // Sprint IoT-1 §247 — clock skew ≤ 5 phút giữa DeviceTimestamp và now.
            if (item.DeviceTimestamp.HasValue)
            {
                var skewMin = Math.Abs((item.DeviceTimestamp.Value.ToUniversalTime() - DateTime.UtcNow).TotalMinutes);
                if (skewMin > 5)
                    AddError(response, $"{prefix}.{nameof(item.DeviceTimestamp)}", $"Clock skew {skewMin:F1} phút > 5 phút. Đồng bộ NTP.");
            }

            if (item.BatteryAssetSerial?.Length > 64)
                AddError(response, $"{prefix}.{nameof(item.BatteryAssetSerial)}", "BatteryAssetSerial tối đa 64 ký tự.");

            if (item.Voltage < 0)
                AddError(response, $"{prefix}.{nameof(item.Voltage)}", "Điện áp không được âm.");

            if (item.Temperature is < -50 or > 120)
                AddError(response, $"{prefix}.{nameof(item.Temperature)}", "Nhiệt độ phải nằm trong khoảng -50 đến 120 độ C.");

            if (item.SocPercent is < 0 or > 100)
                AddError(response, $"{prefix}.{nameof(item.SocPercent)}", "SOC phải nằm trong khoảng 0-100.");

            if (item.CycleCount is < 0)
                AddError(response, $"{prefix}.{nameof(item.CycleCount)}", "Số chu kỳ không được âm.");

            if (item.SourceDeviceId?.Length > 64)
                AddError(response, $"{prefix}.{nameof(item.SourceDeviceId)}", "Id thiết bị nguồn tối đa 64 ký tự.");

            if (item.SohPercent is < 0 or > 100)
                AddError(response, $"{prefix}.{nameof(item.SohPercent)}", "SOH phải nằm trong khoảng 0-100.");

            if (item.ChargingState.HasValue && !Enum.IsDefined(typeof(ChargingStateEnum), item.ChargingState.Value))
                AddError(response, $"{prefix}.{nameof(item.ChargingState)}", "ChargingState không hợp lệ.");

            // Sprint 5B #105 — Tier 2 validation.
            if (item.InternalResistanceMilliohm is <= 0)
                AddError(response, $"{prefix}.{nameof(item.InternalResistanceMilliohm)}", "Internal resistance phải > 0 mΩ.");

            if (item.CellVoltageDeltaMv is < 0)
                AddError(response, $"{prefix}.{nameof(item.CellVoltageDeltaMv)}", "Cell voltage delta không được âm.");

            // Sprint 5B B9 — SourceType + length validation.
            if (!Enum.IsDefined(typeof(SensorReadingSourceTypeEnum), item.SourceType))
                AddError(response, $"{prefix}.{nameof(item.SourceType)}", "SourceType không hợp lệ.");

            if (item.BmsErrorCode?.Length > 64)
                AddError(response, $"{prefix}.{nameof(item.BmsErrorCode)}", "BmsErrorCode tối đa 64 ký tự.");

            if (item.SensorSourceCode?.Length > 20)
                AddError(response, $"{prefix}.{nameof(item.SensorSourceCode)}", "SensorSourceCode tối đa 20 ký tự.");
        }

        return Task.FromResult(response);
    }

    private static void AddError(CommonResponse<SensorReadingBatchIngestResult> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Dữ liệu sensor reading không hợp lệ.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}

public class SensorReadingItem
{
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

    public Guid BatteryAssetId { get; set; }

    public decimal Voltage { get; set; }

    public decimal Current { get; set; }

    public decimal Temperature { get; set; }

    public decimal SocPercent { get; set; }

    public int? CycleCount { get; set; }

    public decimal? SohPercent { get; set; }

    public ChargingStateEnum? ChargingState { get; set; }

    public string? SourceDeviceId { get; set; }

    // Sprint 5B #101/#105 — Tier 2 battery health metrics.
    public decimal? InternalResistanceMilliohm { get; set; }
    public decimal? CellVoltageDeltaMv { get; set; }

    // Sprint 5B B9 (#154) — phân biệt nguồn đo (BMS vs IoT vs External).
    public SensorReadingSourceTypeEnum SourceType { get; set; } = SensorReadingSourceTypeEnum.IotGateway;

    /// <summary>BMS error raw code (vd "0x0A", "OverCurrent,CellImbalance"). Tối đa 64 ký tự.</summary>
    public string? BmsErrorCode { get; set; }

    /// <summary>§52.9 — "primary"/"redundant"/"external-temp". Tối đa 20 ký tự.</summary>
    public string? SensorSourceCode { get; set; }
}
