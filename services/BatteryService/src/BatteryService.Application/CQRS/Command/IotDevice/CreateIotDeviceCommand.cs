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
            AddError(response, nameof(DeviceCode), "Device code is required.");
        else if (code.Length is < 3 or > 64)
            AddError(response, nameof(DeviceCode), "Device code must be 3-64 characters long.");
        else if (!Regex.IsMatch(code, "^[A-Z0-9-]+$", RegexOptions.CultureInvariant))
            AddError(response, nameof(DeviceCode), "Device code may only contain uppercase letters, digits, and hyphens.");

        if (string.IsNullOrWhiteSpace(DisplayName))
            AddError(response, nameof(DisplayName), "Display name is required.");
        else if (DisplayName.Length > 200)
            AddError(response, nameof(DisplayName), "Display name must not exceed 200 characters.");

        if (SiteId == Guid.Empty)
            AddError(response, nameof(SiteId), "SiteId is required.");

        if (HardwareRevision?.Length > 64)
            AddError(response, nameof(HardwareRevision), "HardwareRevision must not exceed 64 characters.");

        if (HeartbeatIntervalSeconds is < 10 or > 3600)
            AddError(response, nameof(HeartbeatIntervalSeconds), "Heartbeat interval must be within [10, 3600] seconds.");

        if (ApiKeyScopes == IotApiKeyScopeEnum.None)
            AddError(response, nameof(ApiKeyScopes), "At least 1 scope must be granted for the API key.");

        if (Notes?.Length > 1000)
            AddError(response, nameof(Notes), "Notes must not exceed 1000 characters.");

        return Task.FromResult(response);
    }

    private static void AddError(CommonResponse<IotDeviceCreatedDto> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Invalid IoT device data.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}
