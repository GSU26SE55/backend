using BatteryService.Application.CQRS.Command.IotDevice;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using IotDeviceEntity = BatteryService.Domain.Entities.IotDevice;

namespace BatteryService.Application.CQRS.Handler.IotDevice;

public class CreateIotDeviceCommandHandler : IRequestHandler<CreateIotDeviceCommand, CommonResponse<IotDeviceCreatedDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IIotApiKeyService _apiKeyService;
    private readonly IMqttBrokerEndpointProvider _brokerEndpoint;
    private readonly IMqttPasswordFileSync _passwordFileSync;   // IOT3-29

    public CreateIotDeviceCommandHandler(
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

    public async Task<CommonResponse<IotDeviceCreatedDto>> Handle(CreateIotDeviceCommand request, CancellationToken cancellationToken)
    {
        var code = request.DeviceCode.Trim().ToUpperInvariant();

        var duplicate = await _unitOfWork.IotDevices
            .GetAllAsync()
            .AnyAsync(d => !d.IsDeleted && d.DeviceCode == code, cancellationToken);
        if (duplicate)
            return new CommonResponse<IotDeviceCreatedDto> { IsSuccess = false, StatusCode = 409, Message = "Device code already exists." };

        var site = await _unitOfWork.Sites.GetAllAsync()
            .FirstOrDefaultAsync(s => s.Id == request.SiteId && !s.IsDeleted, cancellationToken);
        if (site is null)
            return new CommonResponse<IotDeviceCreatedDto> { IsSuccess = false, StatusCode = 404, Message = "Site not found." };

        var key = _apiKeyService.GenerateKey();
        var mqttCred = _apiKeyService.GenerateMqttCredential(code);
        var now = DateTime.UtcNow;

        var entity = new IotDeviceEntity
        {
            Id = Guid.NewGuid(),
            DeviceCode = code,
            DisplayName = request.DisplayName.Trim(),
            SiteId = request.SiteId,
            HardwareRevision = request.HardwareRevision?.Trim(),
            Status = Domain.Enums.IotDeviceStatusEnum.Pending,
            ApiKeyHash = key.Hash,
            ApiKeyPlaintext = key.RawKey,
            ApiKeyLastFour = key.LastFour,
            ApiKeyScopes = request.ApiKeyScopes,
            ApiKeyIssuedAt = now,
            HeartbeatIntervalSeconds = request.HeartbeatIntervalSeconds,
            Notes = request.Notes?.Trim(),
            // Sprint IoT-2 #IoT2-26.
            MqttUsername = mqttCred.Username,
            MqttPasswordHash = mqttCred.PasswordHash
        };

        await _unitOfWork.IotDevices.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // IOT3-31 — mapper lo hết sáu trường MQTT; không còn gán tay ở đây nữa để đường
        // create và đường rotate không thể lệch nhau (rotate từng quên và trả toàn null).
        var broker = _brokerEndpoint.Resolve(entity.DeviceCode);
        var dto = IotDeviceMapper.ToCreatedDto(entity, key.RawKey, site.Name, broker, mqttCred.RawPassword);

        // IOT3-29 — đẩy thông tin đăng nhập xuống broker NGAY, đừng để thiết bị lắp xong mà
        // phải chờ hết vòng quét 60s mới nối được. Hỏng thì vòng quét nền bù, không làm fail create.
        try { await _passwordFileSync.SyncOnceAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { /* vòng quét nền sẽ bù */ }

        return new CommonResponse<IotDeviceCreatedDto>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "IoT device created successfully.",
            Data = dto
        };
    }
}
