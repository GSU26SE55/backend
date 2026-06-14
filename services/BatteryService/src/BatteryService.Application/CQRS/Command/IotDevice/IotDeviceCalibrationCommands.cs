using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.IotDevice;

/// <summary>
/// Sprint IoT-2 #IoT2-32 (S7-BE-01) — tạo calibration profile cho 1 channel của device.
/// </summary>
public class CreateIotDeviceCalibrationCommand : IRequest<CommonResponse<IotDeviceCalibrationDto>>, IValidatable<CommonResponse<IotDeviceCalibrationDto>>
{
    /// <summary>ID IoT device (Guid).</summary>
    [JsonIgnore]
    [BindNever]
    public Guid IotDeviceId { get; set; }

    /// <summary>"voltage" | "current" | "temperature" | "soc" (lowercase).</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>ID BatteryAsset (Guid).</summary>
    public Guid? BatteryAssetId { get; set; }

    /// <summary>Scale factor calibration (default 1.0).</summary>
    public decimal Scale { get; set; } = 1m;

    /// <summary>Offset cộng thêm calibration (default 0.0).</summary>
    public decimal Offset { get; set; }

    /// <summary>Đơn vị đo (V | A | °C | %).</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Ngày calibration thực hiện.</summary>
    public DateTime CalibratedAt { get; set; }

    /// <summary>Ngày hết hạn.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Ghi chú tự do.</summary>
    public string? Notes { get; set; }

    public Task<CommonResponse<IotDeviceCalibrationDto>> ValidateAsync()
    {
        var r = new CommonResponse<IotDeviceCalibrationDto>();

        if (string.IsNullOrWhiteSpace(Channel))
            Add(r, nameof(Channel), "Channel là bắt buộc.");
        else if (Channel.Length > 32)
            Add(r, nameof(Channel), "Channel ≤ 32 ký tự.");

        if (string.IsNullOrWhiteSpace(Unit))
            Add(r, nameof(Unit), "Unit là bắt buộc.");
        else if (Unit.Length > 16)
            Add(r, nameof(Unit), "Unit ≤ 16 ký tự.");

        if (Scale == 0)
            Add(r, nameof(Scale), "Scale phải khác 0.");

        if (CalibratedAt == default)
            Add(r, nameof(CalibratedAt), "CalibratedAt là bắt buộc.");

        if (ExpiresAt is not null && ExpiresAt <= CalibratedAt)
            Add(r, nameof(ExpiresAt), "ExpiresAt phải sau CalibratedAt.");

        if (Notes?.Length > 500)
            Add(r, nameof(Notes), "Notes ≤ 500 ký tự.");

        return Task.FromResult(r);
    }

    private static void Add(CommonResponse<IotDeviceCalibrationDto> r, string field, string detail)
    {
        r.IsSuccess = false;
        r.StatusCode = 400;
        r.Message = "Dữ liệu calibration không hợp lệ.";
        r.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}

/// <summary>Sprint IoT-2 #IoT2-32 — soft-delete calibration.</summary>
public class DeleteIotDeviceCalibrationCommand : IRequest<CommonResponse<object>>
{
    /// <summary>Lấy từ route — body JSON không override.</summary>
    [JsonIgnore]
    public Guid IotDeviceId { get; set; }

    /// <summary>Lấy từ route — body JSON không override.</summary>
    [JsonIgnore]
    public Guid CalibrationId { get; set; }
}
