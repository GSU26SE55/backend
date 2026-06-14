using System.Text.RegularExpressions;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.IotDevice;

public class CreateIotDeviceCommand : IRequest<CommonResponse<IotDeviceCreatedDto>>, IValidatable<CommonResponse<IotDeviceCreatedDto>>
{
    /// <summary>Mã device duy nhất (vd ESP32-001).</summary>
    public string DeviceCode { get; set; } = string.Empty;
    /// <summary>Tên gợi nhớ hiển thị UI.</summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>ID Site (Guid).</summary>
    public Guid SiteId { get; set; }
    /// <summary>Hardware revision (vd "v1.0-S3-MAX485").</summary>
    public string? HardwareRevision { get; set; }
    /// <summary>Bitmask scopes API key (SensorIngest | DeviceHeartbeat | EnvironmentalIngest | FirmwareCheck).</summary>
    public IotApiKeyScopeEnum ApiKeyScopes { get; set; } = IotApiKeyScopeEnum.EdgeDeviceDefault;
    /// <summary>Tần suất heartbeat (giây, default 60).</summary>
    public int HeartbeatIntervalSeconds { get; set; } = 60;
    /// <summary>Ghi chú tự do.</summary>
    public string? Notes { get; set; }

    public Task<CommonResponse<IotDeviceCreatedDto>> ValidateAsync()
    {
        var response = new CommonResponse<IotDeviceCreatedDto>();
        var code = DeviceCode?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(code))
            AddError(response, nameof(DeviceCode), "Device code là bắt buộc.");
        else if (code.Length is < 3 or > 64)
            AddError(response, nameof(DeviceCode), "Device code phải dài 3-64 ký tự.");
        else if (!Regex.IsMatch(code, "^[A-Z0-9-]+$", RegexOptions.CultureInvariant))
            AddError(response, nameof(DeviceCode), "Device code chỉ chứa chữ in hoa, số và dấu gạch ngang.");

        if (string.IsNullOrWhiteSpace(DisplayName))
            AddError(response, nameof(DisplayName), "Tên hiển thị là bắt buộc.");
        else if (DisplayName.Length > 200)
            AddError(response, nameof(DisplayName), "Tên hiển thị tối đa 200 ký tự.");

        if (SiteId == Guid.Empty)
            AddError(response, nameof(SiteId), "SiteId là bắt buộc.");

        if (HardwareRevision?.Length > 64)
            AddError(response, nameof(HardwareRevision), "HardwareRevision tối đa 64 ký tự.");

        if (HeartbeatIntervalSeconds is < 10 or > 3600)
            AddError(response, nameof(HeartbeatIntervalSeconds), "Heartbeat interval phải nằm trong [10, 3600] giây.");

        if (ApiKeyScopes == IotApiKeyScopeEnum.None)
            AddError(response, nameof(ApiKeyScopes), "Phải cấp ít nhất 1 scope cho API key.");

        if (Notes?.Length > 1000)
            AddError(response, nameof(Notes), "Notes tối đa 1000 ký tự.");

        return Task.FromResult(response);
    }

    private static void AddError(CommonResponse<IotDeviceCreatedDto> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Dữ liệu IoT device không hợp lệ.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}
