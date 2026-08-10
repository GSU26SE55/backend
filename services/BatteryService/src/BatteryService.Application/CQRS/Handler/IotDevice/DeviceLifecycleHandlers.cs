using BatteryService.Application.Common;          // IOT3-33: SensorSource.Primary
using BatteryService.Application.CQRS.Command.IotDevice;
using BatteryService.Application.CQRS.Query.IotDevice;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using HeartbeatEntity = BatteryService.Domain.Entities.IotDeviceHeartbeat;
using UpdateLogEntity = BatteryService.Domain.Entities.IotFirmwareUpdateLog;

namespace BatteryService.Application.CQRS.Handler.IotDevice;

/// <summary>
/// Sprint IoT-1 (#245, #247) — Provision: device báo "tôi đã boot, firmware version X".
/// Backend chuyển status Pending→Active, lưu firmware version, ack với heartbeat interval + scopes.
/// </summary>
public class ProvisionIotDeviceCommandHandler : IRequestHandler<ProvisionIotDeviceCommand, CommonResponse<IotDeviceProvisionResultDto>>
{
    private const double ClockSkewWarnThresholdSeconds = 300; // 5 phút (§247).

    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IMqttBrokerEndpointProvider _brokerEndpoint;   // IOT3-27
    private readonly IIotApiKeyService _apiKeyService;              // IOT3-28
    private readonly IMqttPasswordFileSync _passwordFileSync;       // IOT3-29

    public ProvisionIotDeviceCommandHandler(
        IBatteryUnitOfWork uow,
        IMqttBrokerEndpointProvider brokerEndpoint,
        IIotApiKeyService apiKeyService,
        IMqttPasswordFileSync passwordFileSync)
    {
        _unitOfWork = uow;
        _brokerEndpoint = brokerEndpoint;
        _apiKeyService = apiKeyService;
        _passwordFileSync = passwordFileSync;
    }

    public async Task<CommonResponse<IotDeviceProvisionResultDto>> Handle(ProvisionIotDeviceCommand request, CancellationToken ct)
    {
        var device = await _unitOfWork.IotDevices.GetAllAsync()
            .Include(d => d.TargetFirmwareRelease)
            .FirstOrDefaultAsync(d => d.Id == request.DeviceId && !d.IsDeleted, ct);
        if (device is null)
            return new CommonResponse<IotDeviceProvisionResultDto> { IsSuccess = false, StatusCode = 404, Message = "Device không tồn tại." };

        if (!string.Equals(device.DeviceCode, request.DeviceCode, StringComparison.Ordinal))
            return new CommonResponse<IotDeviceProvisionResultDto> { IsSuccess = false, StatusCode = 403, Message = "DeviceCode không khớp với API key." };

        // 409: state conflict — device đang ở trạng thái không cho phép provision.
        if (device.Status == IotDeviceStatusEnum.Disabled || device.Status == IotDeviceStatusEnum.Decommissioned)
            return new CommonResponse<IotDeviceProvisionResultDto> { IsSuccess = false, StatusCode = 409, Message = "Device đang ở trạng thái Disabled/Decommissioned, không thể provision." };

        var now = DateTime.UtcNow;
        var skew = Math.Abs((request.DeviceTimestamp.ToUniversalTime() - now).TotalSeconds);
        if (skew > ClockSkewWarnThresholdSeconds)
        {
            // 422: format đúng nhưng vi phạm business rule (clock skew).
            return new CommonResponse<IotDeviceProvisionResultDto>
            {
                IsSuccess = false,
                StatusCode = 422,
                Message = $"Clock skew {skew:F0}s vượt ngưỡng {ClockSkewWarnThresholdSeconds}s. Đồng bộ NTP trước khi provision."
            };
        }

        // IOT3-28 — tự vá thiết bị chưa có thông tin đăng nhập MQTT.
        //
        // Ba nhóm rơi vào đây: (a) tạo trước #IoT2-26 nên chưa từng có username/hash;
        // (b) tạo sau #IoT2-26 nhưng trước IOT3-25 nên có hash mà KHÔNG có plaintext —
        // hash là PBKDF2 một chiều, không dựng lại mật khẩu được; (c) dữ liệu lỗi.
        //
        // Vá tại đây thay vì viết script backfill: thiết bị không bao giờ boot thì cũng
        // không cần thông tin đăng nhập, và cách này tự đúng cho cả thiết bị mới lẫn cũ.
        var mqttCredentialRegenerated = false;
        if (string.IsNullOrWhiteSpace(device.MqttUsername)
            || string.IsNullOrWhiteSpace(device.MqttPasswordHash)
            || string.IsNullOrWhiteSpace(device.MqttPasswordPlaintext))
        {
            var cred = _apiKeyService.GenerateMqttCredential(device.DeviceCode);
            device.MqttUsername = cred.Username;
            device.MqttPasswordHash = cred.PasswordHash;
            device.MqttPasswordPlaintext = cred.RawPassword;
            mqttCredentialRegenerated = true;
        }

        device.CurrentFirmwareVersion = request.FirmwareVersion.Trim();
        if (!string.IsNullOrWhiteSpace(request.HardwareRevision))
            device.HardwareRevision = request.HardwareRevision.Trim();
        device.Status = IotDeviceStatusEnum.Active;
        device.LastProvisionedAt = now;
        device.LastSeenAt = now;
        device.LastClockSkewSeconds = skew;

        _unitOfWork.IotDevices.UpdateAsync(device);
        await _unitOfWork.SaveChangesAsync(ct);

        // IOT3-29 — đẩy thông tin đăng nhập xuống broker NGAY khi vừa sinh mới, đừng đợi vòng
        // quét 60s. Thiết bị nhận mật khẩu trong chính response này và nối broker sau vài giây.
        //
        // Bọc try-catch: hạ tầng đồng bộ hỏng KHÔNG được làm provision thất bại — thiết bị vẫn
        // dùng được đường HTTPS, và vòng quét nền sẽ thử lại.
        if (mqttCredentialRegenerated)
        {
            try { await _passwordFileSync.SyncOnceAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* vòng quét nền sẽ bù */ }
        }

        // Sprint IoT-2 #IoT2-09 — populate configJson: batteryMappings cho site +
        // calibration profile để device map serial → unitId Modbus + sensorSourceCode.
        var batteryMappings = await _unitOfWork.BatteryAssets.GetAllAsync()
            .Where(a => !a.IsDeleted && a.SiteId == device.SiteId)
            .Select(a => new BatteryMappingEntry
            {
                BatteryAssetSerial = a.SerialNumber
            })
            .ToListAsync(ct);

        // SensorSourceCode lấy từ calibration nếu có.
        var calibrations = await _unitOfWork.IotDeviceCalibrations.GetAllAsync()
            .Where(c => !c.IsDeleted && c.IotDeviceId == device.Id)
            .ToListAsync(ct);
        foreach (var m in batteryMappings)
        {
            var asset = await _unitOfWork.BatteryAssets.GetAllAsync()
                .FirstOrDefaultAsync(a => a.SerialNumber == m.BatteryAssetSerial && a.SiteId == device.SiteId, ct);
            if (asset is null)
                continue;
            // IOT3-33 — `Channel` mang "voltage"/"current"/"temperature" (KÊNH ĐO), còn
            // `SensorSourceCode` phải là "primary"/"redundant"/"external-temp" (NGUỒN ĐO).
            // Gán chéo hai khái niệm khiến thiết bị khai sensorSourceCode = "voltage", và mọi
            // truy vấn tính toán lọc `primary` sẽ rơi bản ghi đó (quy ước 3 nguồn/pin).
            //
            // Calibration KHÔNG quyết định nguồn đo — một pin có thể có calibration cho cả ba
            // kênh mà vẫn chỉ có một nguồn `primary`. Nên luôn trả "primary": đó là nguồn BMS,
            // thứ duy nhất backend biết chắc thiết bị sẽ gửi. Nguồn phụ (redundant từ INA226,
            // external-temp từ DS18B20) do firmware tự gắn nhãn khi dựng payload.
            m.SensorSourceCode = SensorSource.Primary;
        }

        // Scope-driven supported sensors.
        var supported = new List<string> { "voltage", "current", "temperature", "soc" };
        if ((device.ApiKeyScopes & IotApiKeyScopeEnum.EnvironmentalIngest) == IotApiKeyScopeEnum.EnvironmentalIngest)
        {
            supported.Add("ambient-temperature");
            supported.Add("ambient-humidity");
            supported.Add("smoke");
            supported.Add("water-leak");
        }

        // IOT3-27 — điểm kết nối broker. Trả Disabled khi Mqtt:Enabled=false hoặc thiếu Host.
        var broker = _brokerEndpoint.Resolve(device.DeviceCode);

        return new CommonResponse<IotDeviceProvisionResultDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Provision thành công.",
            Data = new IotDeviceProvisionResultDto
            {
                DeviceId = device.Id.ToString(),
                DeviceCode = device.DeviceCode,
                SiteId = device.SiteId.ToString(),
                HeartbeatIntervalSeconds = device.HeartbeatIntervalSeconds,
                ApiKeyScopes = device.ApiKeyScopes,
                TargetFirmwareVersion = device.TargetFirmwareRelease?.Version,
                // IOT3-78 — đọc từ DB thay vì số cứng 10. Admin đổi được qua web (biên [1,600]).
                PollingIntervalSeconds = device.PollingIntervalSeconds,
                NtpServer = "time.google.com",
                BatteryMappings = batteryMappings,
                SupportedSensors = supported,

                // IOT3-27 — cấu hình MQTT. Cả sáu trường ĐỒNG THỜI có hoặc ĐỒNG THỜI null:
                // broker tắt ⇒ thiết bị chạy HTTPS-only, không thử nối rồi thất bại vòng lặp.
                MqttBrokerHost = broker.Host,
                MqttBrokerPort = broker.Host is null ? null : broker.Port,
                MqttUseTls = broker.Host is null ? null : broker.UseTls,
                MqttTopicPrefix = broker.Host is null ? null : broker.TopicPrefix,
                MqttUsername = broker.Host is null ? null : device.MqttUsername,
                MqttPassword = broker.Host is null ? null : device.MqttPasswordPlaintext
            }
        };
    }
}

/// <summary>
/// Sprint IoT-1 (#245, #247) — heartbeat tick: insert heartbeat hypertable row, update LastSeenAt,
/// nếu vừa từ Offline trở lại → set Active. Trả ack có server time + skew.
/// </summary>
public class IotDeviceHeartbeatCommandHandler : IRequestHandler<IotDeviceHeartbeatCommand, CommonResponse<IotHeartbeatAckDto>>
{
    private const double ClockSkewWarnThresholdSeconds = 300;

    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IIotMetricsRecorder _metrics;
    public IotDeviceHeartbeatCommandHandler(IBatteryUnitOfWork uow, IIotMetricsRecorder metrics)
    {
        _unitOfWork = uow;
        _metrics = metrics;
    }

    public async Task<CommonResponse<IotHeartbeatAckDto>> Handle(IotDeviceHeartbeatCommand request, CancellationToken ct)
    {
        var device = await _unitOfWork.IotDevices.GetAllAsync()
            .Include(d => d.TargetFirmwareRelease)
            .FirstOrDefaultAsync(d => d.Id == request.DeviceId && !d.IsDeleted, ct);
        if (device is null)
            return new CommonResponse<IotHeartbeatAckDto> { IsSuccess = false, StatusCode = 404, Message = "Device không tồn tại." };

        // 409: state conflict — device hiện không cho phép heartbeat.
        if (device.ApiKeyRevokedAt.HasValue || device.Status == IotDeviceStatusEnum.Disabled || device.Status == IotDeviceStatusEnum.Decommissioned)
            return new CommonResponse<IotHeartbeatAckDto> { IsSuccess = false, StatusCode = 409, Message = "Device đang ở trạng thái Disabled/Decommissioned hoặc API key đã bị revoke." };

        var now = DateTime.UtcNow;
        var deviceTs = request.DeviceTimestamp.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(request.DeviceTimestamp, DateTimeKind.Utc)
            : request.DeviceTimestamp.ToUniversalTime();
        var skew = (deviceTs - now).TotalSeconds;

        var heartbeat = new HeartbeatEntity
        {
            Time = now,
            IotDeviceId = device.Id,
            FirmwareVersion = request.FirmwareVersion?.Trim(),
            RssiDbm = request.RssiDbm,
            FreeMemoryPercent = request.FreeMemoryPercent,
            UptimeSeconds = request.UptimeSeconds,
            QueuedReadingCount = request.QueuedReadingCount,
            DeviceTimestamp = deviceTs,
            ClockSkewSeconds = skew
        };
        await _unitOfWork.IotDeviceHeartbeats.AddAsync(heartbeat);

        device.LastSeenAt = now;
        device.LastClockSkewSeconds = skew;
        if (!string.IsNullOrWhiteSpace(request.FirmwareVersion))
            device.CurrentFirmwareVersion = request.FirmwareVersion.Trim();
        if (device.Status == IotDeviceStatusEnum.Offline || device.Status == IotDeviceStatusEnum.Pending)
            device.Status = IotDeviceStatusEnum.Active;

        _unitOfWork.IotDevices.UpdateAsync(device);
        await _unitOfWork.SaveChangesAsync(ct);

        // Sprint IoT-2 #IoT2-38 — label status: ok | clock_drift.
        var hbStatus = Math.Abs(skew) > ClockSkewWarnThresholdSeconds ? "clock_drift" : "ok";
        _metrics.HeartbeatRecorded(device.Id.ToString(), hbStatus);

        bool fwAvailable = device.TargetFirmwareRelease is not null
                           && !string.Equals(device.TargetFirmwareRelease.Version, device.CurrentFirmwareVersion, StringComparison.Ordinal);

        return new CommonResponse<IotHeartbeatAckDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new IotHeartbeatAckDto
            {
                ServerTime = now,
                ClockSkewSeconds = skew,
                ClockSkewWarning = Math.Abs(skew) > ClockSkewWarnThresholdSeconds,
                NextHeartbeatInSeconds = device.HeartbeatIntervalSeconds,
                FirmwareUpdateAvailable = fwAvailable
            }
        };
    }
}

public class CheckIotFirmwareUpdateQueryHandler : IRequestHandler<CheckIotFirmwareUpdateQuery, CommonResponse<IotFirmwareCheckDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    public CheckIotFirmwareUpdateQueryHandler(IBatteryUnitOfWork uow) => _unitOfWork = uow;

    public async Task<CommonResponse<IotFirmwareCheckDto>> Handle(CheckIotFirmwareUpdateQuery request, CancellationToken ct)
    {
        var device = await _unitOfWork.IotDevices.GetAllAsync()
            .Include(d => d.TargetFirmwareRelease)
            .FirstOrDefaultAsync(d => d.Id == request.DeviceId && !d.IsDeleted, ct);
        if (device is null)
            return new CommonResponse<IotFirmwareCheckDto> { IsSuccess = false, StatusCode = 404, Message = "Device không tồn tại." };

        var target = device.TargetFirmwareRelease;
        if (target is null || target.IsArchived || !target.IsPublished
            || string.Equals(target.Version, request.CurrentVersion, StringComparison.Ordinal))
        {
            return new CommonResponse<IotFirmwareCheckDto>
            {
                IsSuccess = true,
                StatusCode = 200,
                Data = new IotFirmwareCheckDto { UpdateAvailable = false }
            };
        }

        // Tạo update log Pending.
        var log = new UpdateLogEntity
        {
            Id = Guid.NewGuid(),
            IotDeviceId = device.Id,
            IotFirmwareReleaseId = target.Id,
            FromVersion = request.CurrentVersion,
            ToVersion = target.Version,
            Status = IotFirmwareUpdateStatusEnum.Pending,
            StartedAt = DateTime.UtcNow
        };
        await _unitOfWork.IotFirmwareUpdateLogs.AddAsync(log);
        await _unitOfWork.SaveChangesAsync(ct);

        return new CommonResponse<IotFirmwareCheckDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new IotFirmwareCheckDto
            {
                UpdateAvailable = true,
                TargetVersion = target.Version,
                ArtifactUrl = target.ArtifactUrl,
                Sha256Checksum = target.Sha256Checksum,
                ArtifactSizeBytes = target.ArtifactSizeBytes,
                UpdateLogId = log.Id.ToString(),
                ReleaseNotes = target.ReleaseNotes,
                // Sprint IoT-2 #IoT2-36.
                IsRequired = target.IsRequired,
                Channel = target.Channel
            }
        };
    }
}

public class UpdateIotFirmwareUpdateLogCommandHandler : IRequestHandler<UpdateIotFirmwareUpdateLogCommand, CommonResponse<object>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IIotMetricsRecorder _metrics;
    public UpdateIotFirmwareUpdateLogCommandHandler(IBatteryUnitOfWork uow, IIotMetricsRecorder metrics)
    {
        _unitOfWork = uow;
        _metrics = metrics;
    }

    public async Task<CommonResponse<object>> Handle(UpdateIotFirmwareUpdateLogCommand request, CancellationToken ct)
    {
        var log = await _unitOfWork.IotFirmwareUpdateLogs.GetAllAsync()
            .FirstOrDefaultAsync(l => l.Id == request.LogId && l.IotDeviceId == request.DeviceId && !l.IsDeleted, ct);
        if (log is null)
            return new CommonResponse<object> { IsSuccess = false, StatusCode = 404, Message = "Update log không tồn tại." };

        log.Status = request.Status;
        if (request.BytesDownloaded.HasValue)
            log.BytesDownloaded = request.BytesDownloaded;
        if (!string.IsNullOrWhiteSpace(request.FailureReason))
            log.FailureReason = request.FailureReason.Trim();
        if (request.Status == IotFirmwareUpdateStatusEnum.Success
            || request.Status == IotFirmwareUpdateStatusEnum.Failed
            || request.Status == IotFirmwareUpdateStatusEnum.Skipped)
        {
            log.CompletedAt = DateTime.UtcNow;

            if (request.Status == IotFirmwareUpdateStatusEnum.Success)
            {
                var device = await _unitOfWork.IotDevices.GetAllAsync()
                    .FirstOrDefaultAsync(d => d.Id == request.DeviceId && !d.IsDeleted, ct);
                if (device is not null)
                {
                    device.CurrentFirmwareVersion = log.ToVersion;
                    _unitOfWork.IotDevices.UpdateAsync(device);
                }
            }
        }

        _unitOfWork.IotFirmwareUpdateLogs.UpdateAsync(log);
        await _unitOfWork.SaveChangesAsync(ct);

        // Sprint IoT-2 #IoT2-38 — emit firmware transition metric.
        _metrics.FirmwareUpdateRecorded(
            log.FromVersion ?? "unknown",
            log.ToVersion ?? "unknown",
            request.Status.ToString().ToLowerInvariant());

        return new CommonResponse<object> { IsSuccess = true, StatusCode = 200, Message = "Đã cập nhật log." };
    }
}
