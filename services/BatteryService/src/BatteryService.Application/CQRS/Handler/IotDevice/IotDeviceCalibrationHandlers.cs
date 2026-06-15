using BatteryService.Application.CQRS.Command.IotDevice;
using BatteryService.Application.CQRS.Query.IotDevice;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.IotDevice;

internal static class IotDeviceCalibrationMapper
{
    public static IotDeviceCalibrationDto ToDto(IotDeviceCalibration c) => new()
    {
        Id = c.Id.ToString(),
        IotDeviceId = c.IotDeviceId.ToString(),
        Channel = c.Channel,
        BatteryAssetId = c.BatteryAssetId?.ToString(),
        Scale = c.Scale,
        Offset = c.Offset,
        Unit = c.Unit,
        CalibratedAt = c.CalibratedAt,
        ExpiresAt = c.ExpiresAt,
        Notes = c.Notes,
        CreatedAt = c.CreatedAt
    };
}

public class CreateIotDeviceCalibrationCommandHandler : IRequestHandler<CreateIotDeviceCalibrationCommand, CommonResponse<IotDeviceCalibrationDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IIotCalibrationCache _cache;

    public CreateIotDeviceCalibrationCommandHandler(IBatteryUnitOfWork unitOfWork, IIotCalibrationCache cache)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task<CommonResponse<IotDeviceCalibrationDto>> Handle(CreateIotDeviceCalibrationCommand request, CancellationToken ct)
    {
        var device = await _unitOfWork.IotDevices.GetAllAsync()
            .FirstOrDefaultAsync(d => d.Id == request.IotDeviceId && !d.IsDeleted, ct);
        if (device is null)
            return new CommonResponse<IotDeviceCalibrationDto> { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy device." };

        var entity = new IotDeviceCalibration
        {
            Id = Guid.NewGuid(),
            IotDeviceId = request.IotDeviceId,
            Channel = request.Channel.Trim().ToLowerInvariant(),
            BatteryAssetId = request.BatteryAssetId,
            Scale = request.Scale,
            Offset = request.Offset,
            Unit = request.Unit.Trim(),
            CalibratedAt = request.CalibratedAt,
            ExpiresAt = request.ExpiresAt,
            Notes = request.Notes?.Trim()
        };
        await _unitOfWork.IotDeviceCalibrations.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync(ct);

        // Sprint IoT-2 #IoT2-34 — invalidate cache để reading kế tiếp dùng calibration mới.
        await _cache.InvalidateAsync(request.IotDeviceId, ct);

        return new CommonResponse<IotDeviceCalibrationDto>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Tạo calibration thành công.",
            Data = IotDeviceCalibrationMapper.ToDto(entity)
        };
    }
}

public class DeleteIotDeviceCalibrationCommandHandler : IRequestHandler<DeleteIotDeviceCalibrationCommand, CommonResponse<object>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IIotCalibrationCache _cache;

    public DeleteIotDeviceCalibrationCommandHandler(IBatteryUnitOfWork unitOfWork, IIotCalibrationCache cache)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task<CommonResponse<object>> Handle(DeleteIotDeviceCalibrationCommand request, CancellationToken ct)
    {
        var entity = await _unitOfWork.IotDeviceCalibrations.GetAllAsync()
            .FirstOrDefaultAsync(c => c.Id == request.CalibrationId && c.IotDeviceId == request.IotDeviceId && !c.IsDeleted, ct);
        if (entity is null)
            return new CommonResponse<object> { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy calibration." };

        _unitOfWork.IotDeviceCalibrations.DeleteAsync(entity);
        await _unitOfWork.SaveChangesAsync(ct);
        await _cache.InvalidateAsync(request.IotDeviceId, ct);

        return new CommonResponse<object> { IsSuccess = true, StatusCode = 200, Message = "Đã xoá calibration." };
    }
}

public class GetIotDeviceCalibrationsQueryHandler : IRequestHandler<GetIotDeviceCalibrationsQuery, CommonResponse<List<IotDeviceCalibrationDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    public GetIotDeviceCalibrationsQueryHandler(IBatteryUnitOfWork uow) => _unitOfWork = uow;

    public async Task<CommonResponse<List<IotDeviceCalibrationDto>>> Handle(GetIotDeviceCalibrationsQuery request, CancellationToken ct)
    {
        var q = _unitOfWork.IotDeviceCalibrations.GetAllAsync()
            .Where(c => !c.IsDeleted && c.IotDeviceId == request.IotDeviceId);

        if (!string.IsNullOrWhiteSpace(request.Channel))
        {
            var channel = request.Channel.Trim().ToLowerInvariant();
            q = q.Where(c => c.Channel == channel);
        }
        if (!request.IncludeExpired)
        {
            var now = DateTime.UtcNow;
            q = q.Where(c => c.ExpiresAt == null || c.ExpiresAt > now);
        }

        var list = await q.OrderByDescending(c => c.CalibratedAt).ToListAsync(ct);
        return new CommonResponse<List<IotDeviceCalibrationDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = list.Select(IotDeviceCalibrationMapper.ToDto).ToList()
        };
    }
}

public class GetIotCalibrationsExpiringQueryHandler : IRequestHandler<GetIotCalibrationsExpiringQuery, CommonResponse<List<IotDeviceCalibrationDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    public GetIotCalibrationsExpiringQueryHandler(IBatteryUnitOfWork uow) => _unitOfWork = uow;

    public async Task<CommonResponse<List<IotDeviceCalibrationDto>>> Handle(GetIotCalibrationsExpiringQuery request, CancellationToken ct)
    {
        var withinDays = Math.Clamp(request.WithinDays, 1, 365);
        var now = DateTime.UtcNow;
        var boundary = now.AddDays(withinDays);

        var list = await _unitOfWork.IotDeviceCalibrations.GetAllAsync()
            .Where(c => !c.IsDeleted && c.ExpiresAt != null && c.ExpiresAt > now && c.ExpiresAt <= boundary)
            .OrderBy(c => c.ExpiresAt)
            .ToListAsync(ct);

        return new CommonResponse<List<IotDeviceCalibrationDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = list.Select(IotDeviceCalibrationMapper.ToDto).ToList()
        };
    }
}
