using BatteryService.Application.CQRS.Command.IotDevice;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.IotDevice;

public class UpdateIotDeviceCommandHandler : IRequestHandler<UpdateIotDeviceCommand, CommonResponse<IotDeviceDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    public UpdateIotDeviceCommandHandler(IBatteryUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<IotDeviceDto>> Handle(UpdateIotDeviceCommand request, CancellationToken ct)
    {
        var entity = await _unitOfWork.IotDevices.GetAllAsync()
            .Include(d => d.Site)
            .Include(d => d.TargetFirmwareRelease)
            .FirstOrDefaultAsync(d => d.Id == request.Id && !d.IsDeleted, ct);
        if (entity is null)
            return new CommonResponse<IotDeviceDto> { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy device." };

        if (entity.SiteId != request.SiteId)
        {
            var site = await _unitOfWork.Sites.GetAllAsync()
                .FirstOrDefaultAsync(s => s.Id == request.SiteId && !s.IsDeleted, ct);
            if (site is null)
                return new CommonResponse<IotDeviceDto> { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy Site." };
            entity.SiteId = site.Id;
            entity.Site = site;
        }

        if (request.TargetFirmwareReleaseId.HasValue)
        {
            var fw = await _unitOfWork.IotFirmwareReleases.GetAllAsync()
                .FirstOrDefaultAsync(f => f.Id == request.TargetFirmwareReleaseId.Value && !f.IsDeleted, ct);
            if (fw is null)
                return new CommonResponse<IotDeviceDto> { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy firmware release." };
            // 409: release tồn tại nhưng đang ở trạng thái không cho phép đặt làm target.
            if (!fw.IsPublished || fw.IsArchived)
                return new CommonResponse<IotDeviceDto> { IsSuccess = false, StatusCode = 409, Message = "Firmware release chưa publish hoặc đã archived, không thể đặt làm target." };
            entity.TargetFirmwareReleaseId = fw.Id;
            entity.TargetFirmwareRelease = fw;
        }
        else
        {
            entity.TargetFirmwareReleaseId = null;
            entity.TargetFirmwareRelease = null;
        }

        entity.DisplayName = request.DisplayName.Trim();
        entity.HardwareRevision = request.HardwareRevision?.Trim();
        entity.Status = request.Status;
        entity.ApiKeyScopes = request.ApiKeyScopes;
        entity.HeartbeatIntervalSeconds = request.HeartbeatIntervalSeconds;
        entity.Notes = request.Notes?.Trim();

        _unitOfWork.IotDevices.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(ct);

        return new CommonResponse<IotDeviceDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cập nhật device thành công.",
            Data = IotDeviceMapper.ToDto(entity, entity.Site?.Name, entity.TargetFirmwareRelease?.Version)
        };
    }
}

public class DeleteIotDeviceCommandHandler : IRequestHandler<DeleteIotDeviceCommand, CommonResponse<object>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    public DeleteIotDeviceCommandHandler(IBatteryUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<object>> Handle(DeleteIotDeviceCommand request, CancellationToken ct)
    {
        var entity = await _unitOfWork.IotDevices.GetAllAsync()
            .FirstOrDefaultAsync(d => d.Id == request.Id && !d.IsDeleted, ct);
        if (entity is null)
            return new CommonResponse<object> { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy device." };

        entity.Status = IotDeviceStatusEnum.Decommissioned;
        entity.ApiKeyRevokedAt = DateTime.UtcNow;
        _unitOfWork.IotDevices.DeleteAsync(entity);
        await _unitOfWork.SaveChangesAsync(ct);

        return new CommonResponse<object> { IsSuccess = true, StatusCode = 200, Message = "Đã decommission device." };
    }
}

public class RotateIotDeviceApiKeyCommandHandler : IRequestHandler<RotateIotDeviceApiKeyCommand, CommonResponse<IotDeviceCreatedDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IIotApiKeyService _apiKeyService;
    private readonly IMqttBrokerEndpointProvider _brokerEndpoint;   // IOT3-30
    private readonly IMqttPasswordFileSync _passwordFileSync;       // IOT3-30

    public RotateIotDeviceApiKeyCommandHandler(
        IBatteryUnitOfWork unitOfWork,
        IIotApiKeyService apiKeyService,
        IMqttBrokerEndpointProvider brokerEndpoint,
        IMqttPasswordFileSync passwordFileSync)
    {
        _unitOfWork = unitOfWork;
        _apiKeyService = apiKeyService;
        _brokerEndpoint = brokerEndpoint;
        _passwordFileSync = passwordFileSync;
    }

    public async Task<CommonResponse<IotDeviceCreatedDto>> Handle(RotateIotDeviceApiKeyCommand request, CancellationToken ct)
    {
        var entity = await _unitOfWork.IotDevices.GetAllAsync()
            .Include(d => d.Site)
            .FirstOrDefaultAsync(d => d.Id == request.Id && !d.IsDeleted, ct);
        if (entity is null)
            return new CommonResponse<IotDeviceCreatedDto> { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy device." };

        var key = _apiKeyService.GenerateKey();
        entity.ApiKeyHash = key.Hash;
        entity.ApiKeyPlaintext = key.RawKey;
        entity.ApiKeyLastFour = key.LastFour;
        entity.ApiKeyIssuedAt = DateTime.UtcNow;
        entity.ApiKeyRevokedAt = null;

        // IOT3-30 — xoay LUÔN thông tin đăng nhập MQTT.
        //
        // Trước đây rotate-key chỉ đổi apiKey, để nguyên credential MQTT. Thiết bị vì thế mất
        // đường HTTPS (401) nhưng vẫn publish MQTT được bằng mật khẩu cũ — nửa sống nửa chết,
        // và "xoay khoá" không thật sự thu hồi được gì. Thao tác này vốn đã buộc phải ra hiện
        // trường nạp lại apiKey, nên xoay cả hai cùng lúc không tốn thêm chuyến đi nào.
        //
        // Muốn xoay riêng phần MQTT mà thiết bị TỰ LÀNH thì dùng rotate-mqtt (IOT3-32).
        var mqttCred = _apiKeyService.GenerateMqttCredential(entity.DeviceCode);
        entity.MqttUsername = mqttCred.Username;
        entity.MqttPasswordHash = mqttCred.PasswordHash;
        entity.MqttPasswordPlaintext = mqttCred.RawPassword;

        _unitOfWork.IotDevices.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(ct);

        try { await _passwordFileSync.SyncOnceAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { /* vòng quét nền sẽ bù */ }

        return new CommonResponse<IotDeviceCreatedDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Rotate API key + MQTT credential thành công. Thiết bị PHẢI được nạp lại apiKey tại chỗ.",
            Data = IotDeviceMapper.ToCreatedDto(
                entity, key.RawKey, entity.Site?.Name,
                _brokerEndpoint.Resolve(entity.DeviceCode), mqttCred.RawPassword)
        };
    }
}

/// <summary>IOT3-32 — xoay riêng thông tin đăng nhập MQTT; thiết bị tự lành qua re-provision.</summary>
public class RotateIotDeviceMqttCredentialCommandHandler
    : IRequestHandler<RotateIotDeviceMqttCredentialCommand, CommonResponse<IotDeviceCreatedDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IIotApiKeyService _apiKeyService;
    private readonly IMqttBrokerEndpointProvider _brokerEndpoint;
    private readonly IMqttPasswordFileSync _passwordFileSync;

    public RotateIotDeviceMqttCredentialCommandHandler(
        IBatteryUnitOfWork unitOfWork,
        IIotApiKeyService apiKeyService,
        IMqttBrokerEndpointProvider brokerEndpoint,
        IMqttPasswordFileSync passwordFileSync)
    {
        _unitOfWork = unitOfWork;
        _apiKeyService = apiKeyService;
        _brokerEndpoint = brokerEndpoint;
        _passwordFileSync = passwordFileSync;
    }

    public async Task<CommonResponse<IotDeviceCreatedDto>> Handle(
        RotateIotDeviceMqttCredentialCommand request, CancellationToken ct)
    {
        var entity = await _unitOfWork.IotDevices.GetAllAsync()
            .Include(d => d.Site)
            .FirstOrDefaultAsync(d => d.Id == request.Id && !d.IsDeleted, ct);
        if (entity is null)
            return new CommonResponse<IotDeviceCreatedDto> { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy device." };

        var mqttCred = _apiKeyService.GenerateMqttCredential(entity.DeviceCode);
        entity.MqttUsername = mqttCred.Username;
        entity.MqttPasswordHash = mqttCred.PasswordHash;
        entity.MqttPasswordPlaintext = mqttCred.RawPassword;

        // KHÔNG đụng ApiKeyHash/ApiKeyPlaintext — đó chính là điểm khác rotate-key: apiKey còn
        // hiệu lực nên thiết bị vẫn gọi được /provision để tự lấy mật khẩu MQTT mới.
        _unitOfWork.IotDevices.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(ct);

        try { await _passwordFileSync.SyncOnceAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { /* vòng quét nền sẽ bù */ }

        return new CommonResponse<IotDeviceCreatedDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Rotate MQTT credential thành công. Thiết bị sẽ tự re-provision để lấy mật khẩu mới.",
            Data = IotDeviceMapper.ToCreatedDto(
                entity,
                // apiKey KHÔNG đổi — trả lại bản đang lưu để admin không tưởng là mất.
                entity.ApiKeyPlaintext ?? string.Empty,
                entity.Site?.Name,
                _brokerEndpoint.Resolve(entity.DeviceCode),
                mqttCred.RawPassword)
        };
    }
}

public class RevokeIotDeviceApiKeyCommandHandler : IRequestHandler<RevokeIotDeviceApiKeyCommand, CommonResponse<object>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    public RevokeIotDeviceApiKeyCommandHandler(IBatteryUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<object>> Handle(RevokeIotDeviceApiKeyCommand request, CancellationToken ct)
    {
        var entity = await _unitOfWork.IotDevices.GetAllAsync()
            .FirstOrDefaultAsync(d => d.Id == request.Id && !d.IsDeleted, ct);
        if (entity is null)
            return new CommonResponse<object> { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy device." };

        entity.ApiKeyRevokedAt = DateTime.UtcNow;
        entity.Status = IotDeviceStatusEnum.Disabled;
        _unitOfWork.IotDevices.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(ct);
        return new CommonResponse<object> { IsSuccess = true, StatusCode = 200, Message = "Đã revoke API key." };
    }
}
