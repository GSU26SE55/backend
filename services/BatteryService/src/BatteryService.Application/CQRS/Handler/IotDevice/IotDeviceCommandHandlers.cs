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
    public RotateIotDeviceApiKeyCommandHandler(IBatteryUnitOfWork unitOfWork, IIotApiKeyService apiKeyService)
    {
        _unitOfWork = unitOfWork;
        _apiKeyService = apiKeyService;
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
        _unitOfWork.IotDevices.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(ct);

        return new CommonResponse<IotDeviceCreatedDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Rotate API key thành công.",
            Data = IotDeviceMapper.ToCreatedDto(entity, key.RawKey, entity.Site?.Name)
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
