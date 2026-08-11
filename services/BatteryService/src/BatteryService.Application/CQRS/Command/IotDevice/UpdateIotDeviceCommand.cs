using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.IotDevice;

public class UpdateIotDeviceCommand : IRequest<CommonResponse<IotDeviceDto>>, IValidatable<CommonResponse<IotDeviceDto>>
{
    /// <summary>Lấy từ route — không bind body để tránh client override Id.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }
    /// <summary>Tên gợi nhớ hiển thị UI.</summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>ID Site (Guid).</summary>
    public Guid SiteId { get; set; }
    /// <summary>Hardware revision (vd "v1.0-S3-MAX485").</summary>
    public string? HardwareRevision { get; set; }
    /// <summary>Filter theo status enum.</summary>
    public IotDeviceStatusEnum Status { get; set; }
    /// <summary>Bitmask scopes API key (SensorIngest | DeviceHeartbeat | EnvironmentalIngest | FirmwareCheck).</summary>
    public IotApiKeyScopeEnum ApiKeyScopes { get; set; } = IotApiKeyScopeEnum.EdgeDeviceDefault;
    /// <summary>Tần suất heartbeat (giây, default 60).</summary>
    public int HeartbeatIntervalSeconds { get; set; } = 60;
    /// <summary>ID firmware release đang đặt làm target OTA (nullable).</summary>
    public Guid? TargetFirmwareReleaseId { get; set; }
    /// <summary>Ghi chú tự do.</summary>
    public string? Notes { get; set; }

    public Task<CommonResponse<IotDeviceDto>> ValidateAsync()
    {
        var response = new CommonResponse<IotDeviceDto>();

        if (Id == Guid.Empty)
            AddError(response, nameof(Id), "Id is required.");
        if (string.IsNullOrWhiteSpace(DisplayName))
            AddError(response, nameof(DisplayName), "Display name is required.");
        else if (DisplayName.Length > 200)
            AddError(response, nameof(DisplayName), "Display name must not exceed 200 characters.");
        if (SiteId == Guid.Empty)
            AddError(response, nameof(SiteId), "SiteId is required.");
        if (HeartbeatIntervalSeconds is < 10 or > 3600)
            AddError(response, nameof(HeartbeatIntervalSeconds), "Heartbeat interval must be within [10, 3600] seconds.");
        if (ApiKeyScopes == IotApiKeyScopeEnum.None)
            AddError(response, nameof(ApiKeyScopes), "At least 1 scope must be granted for the API key.");
        if (!Enum.IsDefined(typeof(IotDeviceStatusEnum), Status))
            AddError(response, nameof(Status), "Invalid Status.");
        if (HardwareRevision?.Length > 64)
            AddError(response, nameof(HardwareRevision), "HardwareRevision must not exceed 64 characters.");
        if (Notes?.Length > 1000)
            AddError(response, nameof(Notes), "Notes must not exceed 1000 characters.");

        return Task.FromResult(response);
    }

    private static void AddError(CommonResponse<IotDeviceDto> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Invalid IoT device data.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}

public class DeleteIotDeviceCommand : IRequest<CommonResponse<object>>
{
    /// <summary>Định danh resource.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }
}

public class RotateIotDeviceApiKeyCommand : IRequest<CommonResponse<IotDeviceCreatedDto>>
{
    /// <summary>Định danh resource.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }
}

/// <summary>
/// IOT3-32 — xoay RIÊNG thông tin đăng nhập MQTT, KHÔNG đụng API key.
/// </summary>
/// <remarks>
/// <para>
/// Tách khỏi <see cref="RotateIotDeviceApiKeyCommand"/> vì hai thao tác có hậu quả khác hẳn nhau:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>rotate-key</b> đổi <c>apiKey</c> ⇒ thiết bị mất CẢ HAI đường. HTTPS trả 401 nên nó không
///     provision lại được để lấy khoá mới — <b>không tự lành</b>, phải ra hiện trường nạp lại.
///   </description></item>
///   <item><description>
///     <b>rotate-mqtt</b> chỉ đổi mật khẩu MQTT ⇒ thiết bị bị broker từ chối (<c>state=4</c>),
///     đếm đủ ngưỡng thì tự gọi lại <c>/provision</c> bằng apiKey CŨ vẫn còn hiệu lực và nhận
///     mật khẩu mới. <b>Tự lành, không cần chạm thiết bị.</b>
///   </description></item>
/// </list>
/// <para>Vì vậy thao tác thường dùng khi nghi ngờ lộ credential MQTT là cái này, không phải rotate-key.</para>
/// </remarks>
public class RotateIotDeviceMqttCredentialCommand : IRequest<CommonResponse<IotDeviceCreatedDto>>
{
    /// <summary>Định danh resource.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }
}

public class RevokeIotDeviceApiKeyCommand : IRequest<CommonResponse<object>>
{
    /// <summary>Định danh resource.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }
}
