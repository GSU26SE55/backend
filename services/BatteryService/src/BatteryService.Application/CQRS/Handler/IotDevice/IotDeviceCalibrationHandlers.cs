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
            return new CommonResponse<IotDeviceCalibrationDto> { IsSuccess = false, StatusCode = 404, Message = "Device not found." };

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
            Message = "Calibration created successfully.",
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
            return new CommonResponse<object> { IsSuccess = false, StatusCode = 404, Message = "Calibration not found." };

        _unitOfWork.IotDeviceCalibrations.DeleteAsync(entity);
        await _unitOfWork.SaveChangesAsync(ct);
        await _cache.InvalidateAsync(request.IotDeviceId, ct);

        return new CommonResponse<object> { IsSuccess = true, StatusCode = 200, Message = "Calibration deleted." };
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

        // Kèm serial pin: thẻ hiệu chuẩn trên mobile hiện dòng "Battery: …", trước đây in GUID.
        // Một truy vấn cho cả lô — nhiều channel của cùng thiết bị thường trỏ về cùng một pin.
        var assetIds = list
            .Where(c => c.BatteryAssetId.HasValue)
            .Select(c => c.BatteryAssetId!.Value)
            .Distinct()
            .ToList();

        var serials = assetIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _unitOfWork.BatteryAssets.GetAllAsync()
                .Where(a => !a.IsDeleted && assetIds.Contains(a.Id))
                .Select(a => new { a.Id, a.SerialNumber })
                .ToDictionaryAsync(a => a.Id, a => a.SerialNumber, ct);

        var data = list.Select(c =>
        {
            var dto = IotDeviceCalibrationMapper.ToDto(c);
            // Thiếu serial (pin đã xoá) KHÔNG loại dòng — hồ sơ hiệu chuẩn vẫn phải xem được.
            dto.BatterySerialNumber = c.BatteryAssetId.HasValue
                ? serials.GetValueOrDefault(c.BatteryAssetId.Value)
                : null;
            return dto;
        }).ToList();

        return new CommonResponse<List<IotDeviceCalibrationDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = data
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

        // Kèm mã thiết bị: đây là danh sách gộp NHIỀU thiết bị, nên mỗi dòng phải tự nói được
        // nó thuộc thiết bị nào. Chỉ riêng endpoint này cần — các endpoint calibration khác đều
        // nằm trong ngữ cảnh một thiết bị đã biết.
        // Một truy vấn cho cả lô thay vì mỗi dòng một lượt: nhiều channel chung một thiết bị.
        var deviceIds = list.Select(c => c.IotDeviceId).Distinct().ToList();

        var deviceCodes = await _unitOfWork.IotDevices.GetAllAsync()
            .Where(d => !d.IsDeleted && deviceIds.Contains(d.Id))
            .Select(d => new { d.Id, d.DeviceCode })
            .ToDictionaryAsync(d => d.Id, d => d.DeviceCode, ct);

        var data = list.Select(c =>
        {
            var dto = IotDeviceCalibrationMapper.ToDto(c);
            // Thiếu mã (thiết bị đã xoá) KHÔNG loại dòng: calibration sắp hết hạn vẫn phải
            // được nhìn thấy. FE lùi về IotDeviceId.
            dto.IotDeviceCode = deviceCodes.GetValueOrDefault(c.IotDeviceId);
            return dto;
        }).ToList();

        return new CommonResponse<List<IotDeviceCalibrationDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = data
        };
    }
}
