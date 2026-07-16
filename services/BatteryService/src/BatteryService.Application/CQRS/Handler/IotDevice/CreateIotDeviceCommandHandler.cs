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

    public CreateIotDeviceCommandHandler(IBatteryUnitOfWork unitOfWork, IIotApiKeyService apiKeyService)
    {
        _unitOfWork = unitOfWork;
        _apiKeyService = apiKeyService;
    }

    public async Task<CommonResponse<IotDeviceCreatedDto>> Handle(CreateIotDeviceCommand request, CancellationToken cancellationToken)
    {
        var code = request.DeviceCode.Trim().ToUpperInvariant();

        var duplicate = await _unitOfWork.IotDevices
            .GetAllAsync()
            .AnyAsync(d => !d.IsDeleted && d.DeviceCode == code, cancellationToken);
        if (duplicate)
            return new CommonResponse<IotDeviceCreatedDto> { IsSuccess = false, StatusCode = 409, Message = "Device code đã tồn tại." };

        var site = await _unitOfWork.Sites.GetAllAsync()
            .FirstOrDefaultAsync(s => s.Id == request.SiteId && !s.IsDeleted, cancellationToken);
        if (site is null)
            return new CommonResponse<IotDeviceCreatedDto> { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy Site." };

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

        var dto = IotDeviceMapper.ToCreatedDto(entity, key.RawKey, site.Name);
        dto.MqttUsername = mqttCred.Username;
        dto.MqttPassword = mqttCred.RawPassword;
        return new CommonResponse<IotDeviceCreatedDto>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Tạo IoT device thành công.",
            Data = dto
        };
    }
}
