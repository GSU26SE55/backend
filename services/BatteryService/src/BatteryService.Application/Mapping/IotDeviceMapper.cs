using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;   // IOT3-31: MqttBrokerEndpoint
using BatteryService.Domain.Entities;

namespace BatteryService.Application.Mapping;

public static class IotDeviceMapper
{
    public static IotDeviceDto ToDto(IotDevice e, string? siteName = null, string? targetFirmwareVersion = null) => new()
    {
        Id = e.Id.ToString(),
        DeviceCode = e.DeviceCode,
        DisplayName = e.DisplayName,
        SiteId = e.SiteId.ToString(),
        SiteName = siteName,
        HardwareRevision = e.HardwareRevision,
        Status = e.Status,
        CurrentFirmwareVersion = e.CurrentFirmwareVersion,
        TargetFirmwareReleaseId = e.TargetFirmwareReleaseId?.ToString(),
        TargetFirmwareVersion = targetFirmwareVersion,
        ApiKeyScopes = e.ApiKeyScopes,
        ApiKeyLastFour = e.ApiKeyLastFour,
        ApiKeyIssuedAt = e.ApiKeyIssuedAt,
        ApiKeyRevokedAt = e.ApiKeyRevokedAt,
        LastSeenAt = e.LastSeenAt,
        LastProvisionedAt = e.LastProvisionedAt,
        LastOfflineAt = e.LastOfflineAt,
        HeartbeatIntervalSeconds = e.HeartbeatIntervalSeconds,
        LastClockSkewSeconds = e.LastClockSkewSeconds,
        Notes = e.Notes,
        CreatedAt = e.CreatedAt
    };

    /// <summary>
    /// Map cho GET by id — full base DTO + plaintext <c>ApiKey</c>. Chỉ dùng ở admin GetById,
    /// KHÔNG dùng cho list/get-by-code (tránh lộ key).
    /// </summary>
    public static IotDeviceDetailDto ToDetailDto(
        IotDevice e,
        string? siteName = null,
        string? targetFirmwareVersion = null,
        MqttBrokerEndpoint? broker = null)
    {
        var dto = ToDto(e, siteName, targetFirmwareVersion);
        var hasBroker = broker is { Host: not null };
        return new IotDeviceDetailDto
        {
            Id = dto.Id,
            DeviceCode = dto.DeviceCode,
            DisplayName = dto.DisplayName,
            SiteId = dto.SiteId,
            SiteName = dto.SiteName,
            HardwareRevision = dto.HardwareRevision,
            Status = dto.Status,
            CurrentFirmwareVersion = dto.CurrentFirmwareVersion,
            TargetFirmwareReleaseId = dto.TargetFirmwareReleaseId,
            TargetFirmwareVersion = dto.TargetFirmwareVersion,
            ApiKeyScopes = dto.ApiKeyScopes,
            ApiKeyLastFour = dto.ApiKeyLastFour,
            ApiKeyIssuedAt = dto.ApiKeyIssuedAt,
            ApiKeyRevokedAt = dto.ApiKeyRevokedAt,
            LastSeenAt = dto.LastSeenAt,
            LastProvisionedAt = dto.LastProvisionedAt,
            LastOfflineAt = dto.LastOfflineAt,
            HeartbeatIntervalSeconds = dto.HeartbeatIntervalSeconds,
            LastClockSkewSeconds = dto.LastClockSkewSeconds,
            Notes = dto.Notes,
            CreatedAt = dto.CreatedAt,
            ApiKey = e.ApiKeyPlaintext,

            // IOT3-71 — dựng LẠI chuỗi QR từ key đã lưu, dùng ĐÚNG công thức của `ToCreatedDto`.
            // Không có key thì không có QR: QR chứa key nên dựng từ chuỗi rỗng sẽ ra một mã quét
            // được nhưng nạp vào thiết bị lại không dùng được — sai kiểu im lặng, tệ hơn là thiếu.
            ProvisioningQrCode = string.IsNullOrEmpty(e.ApiKeyPlaintext)
                ? null
                : BuildProvisioningQrCode(e.DeviceCode, e.ApiKeyPlaintext),

            // Cùng quy tắc hai nhóm như `ToCreatedDto` — xem ghi chú ở đó.
            MqttUsername = e.MqttUsername,
            MqttPassword = e.MqttPasswordPlaintext,
            MqttBrokerHost = broker?.Host,
            MqttBrokerPort = hasBroker ? broker!.Value.Port : null,
            MqttUseTls = hasBroker ? broker!.Value.UseTls : null,
            MqttTopicPrefix = hasBroker ? broker!.Value.TopicPrefix : null
        };
    }

    /// <summary>
    /// IOT3-71 — công thức DUY NHẤT dựng chuỗi QR nạp thiết bị.
    /// </summary>
    /// <remarks>
    /// Tách ra vì nay có HAI đường sinh (lúc tạo và lúc xem lại). Hai bản sao của cùng một công
    /// thức sẽ lệch nhau sau lần sửa đầu tiên, và triệu chứng là QR quét được nhưng thiết bị
    /// không nhận — không có gì báo lỗi ở giữa.
    /// </remarks>
    public static string BuildProvisioningQrCode(string deviceCode, string rawApiKey)
        => $"iot://provision?dc={Uri.EscapeDataString(deviceCode)}&key={Uri.EscapeDataString(rawApiKey)}";

    /// <summary>
    /// IOT3-31 — dựng DTO create/rotate, GỒM CẢ sáu trường MQTT.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Trước IOT3-31 mapper này bỏ trắng mọi trường <c>Mqtt*</c>; <c>CreateIotDeviceCommandHandler</c>
    /// gán thủ công sau khi gọi, còn <c>RotateIotDeviceApiKeyCommandHandler</c> thì quên — nên
    /// admin xoay khoá xong nhận về DTO có sáu trường MQTT toàn <c>null</c> mà không ai báo lỗi.
    /// Gộp vào đây để hai đường không thể lệch nhau nữa.
    /// </para>
    /// <para>
    /// <paramref name="rawMqttPassword"/> để <c>null</c> khi lời gọi không xoay mật khẩu MQTT
    /// (ví dụ <c>rotate-mqtt</c> chỉ đổi phần MQTT, hoặc đọc lại thông tin đã lưu) — khi đó
    /// lấy từ <c>e.MqttPasswordPlaintext</c>.
    /// </para>
    /// </remarks>
    public static IotDeviceCreatedDto ToCreatedDto(
        IotDevice e,
        string rawApiKey,
        string? siteName = null,
        MqttBrokerEndpoint? broker = null,
        string? rawMqttPassword = null)
    {
        var dto = ToDto(e, siteName);
        var hasBroker = broker is { Host: not null };
        return new IotDeviceCreatedDto
        {
            Id = dto.Id,
            DeviceCode = dto.DeviceCode,
            DisplayName = dto.DisplayName,
            SiteId = dto.SiteId,
            SiteName = dto.SiteName,
            HardwareRevision = dto.HardwareRevision,
            Status = dto.Status,
            CurrentFirmwareVersion = dto.CurrentFirmwareVersion,
            TargetFirmwareReleaseId = dto.TargetFirmwareReleaseId,
            TargetFirmwareVersion = dto.TargetFirmwareVersion,
            ApiKeyScopes = dto.ApiKeyScopes,
            ApiKeyLastFour = dto.ApiKeyLastFour,
            ApiKeyIssuedAt = dto.ApiKeyIssuedAt,
            ApiKeyRevokedAt = dto.ApiKeyRevokedAt,
            LastSeenAt = dto.LastSeenAt,
            LastProvisionedAt = dto.LastProvisionedAt,
            LastOfflineAt = dto.LastOfflineAt,
            HeartbeatIntervalSeconds = dto.HeartbeatIntervalSeconds,
            LastClockSkewSeconds = dto.LastClockSkewSeconds,
            Notes = dto.Notes,
            CreatedAt = dto.CreatedAt,
            RawApiKey = rawApiKey,
            // Sprint IoT-2 #IoT2-07 — provisioning URL để Admin in QR code.
            ProvisioningQrCode = BuildProvisioningQrCode(e.DeviceCode, rawApiKey),

            // IOT3-31 — sáu trường MQTT, nhưng CHIA HAI NHÓM có quy tắc khác nhau.
            //
            // Nhóm 1 — thông tin đăng nhập (username/password): LUÔN trả nếu đã có trong DB.
            //   Đây là DTO cho ADMIN, không phải cho thiết bị. Credential đã sinh và đã lưu;
            //   giấu đi chỉ vì broker đang tắt là vô ích — admin hoàn toàn có thể tạo hàng loạt
            //   thiết bị trước rồi mới bật MQTT.
            //
            // Nhóm 2 — điểm kết nối (host/port/tls/prefix): null khi broker tắt, vì lúc đó
            //   THẬT SỰ chưa có địa chỉ nào để đưa.
            //
            // ⚠️ Khác hẳn IotDeviceProvisionResultDto (DTO cho THIẾT BỊ): ở đó cả sáu phải cùng
            //    null, vì thiết bị nhận nửa vời sẽ thử nối rồi thất bại trong vòng lặp.
            MqttUsername = e.MqttUsername,
            MqttPassword = rawMqttPassword ?? e.MqttPasswordPlaintext,
            MqttBrokerHost = broker?.Host,
            MqttBrokerPort = hasBroker ? broker!.Value.Port : null,
            MqttUseTls = hasBroker ? broker!.Value.UseTls : null,
            MqttTopicPrefix = hasBroker ? broker!.Value.TopicPrefix : null
        };
    }

    public static IotFirmwareReleaseDto ToDto(IotFirmwareRelease e) => new()
    {
        Id = e.Id.ToString(),
        Version = e.Version,
        HardwareRevision = e.HardwareRevision,
        ArtifactUrl = e.ArtifactUrl,
        Sha256Checksum = e.Sha256Checksum,
        ArtifactSizeBytes = e.ArtifactSizeBytes,
        ReleaseNotes = e.ReleaseNotes,
        IsPublished = e.IsPublished,
        PublishedAt = e.PublishedAt,
        IsArchived = e.IsArchived,
        CreatedAt = e.CreatedAt,
        // Sprint IoT-2 #IoT2-35.
        IsRequired = e.IsRequired,
        Channel = e.Channel,
        DeviceModel = e.DeviceModel
    };
}
