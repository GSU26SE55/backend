using BatteryService.Application.CQRS.Query.IotDevice;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.IotDevice;

public class GetIotDevicesQueryHandler : IRequestHandler<GetIotDevicesQuery, CommonResponse<PaginationResponse<IotDeviceDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    public GetIotDevicesQueryHandler(IBatteryUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<PaginationResponse<IotDeviceDto>>> Handle(GetIotDevicesQuery request, CancellationToken ct)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var size = Math.Clamp(request.PageSize, 1, 100);

        var query = _unitOfWork.IotDevices.GetAllAsync()
            .Include(d => d.Site)
            .Include(d => d.TargetFirmwareRelease)
            .Where(d => !d.IsDeleted);

        if (request.SiteId.HasValue)
            query = query.Where(d => d.SiteId == request.SiteId.Value);
        if (request.Status.HasValue)
            query = query.Where(d => d.Status == request.Status.Value);
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim().ToLowerInvariant();
            query = query.Where(d => d.DeviceCode.ToLower().Contains(kw) || d.DisplayName.ToLower().Contains(kw));
        }

        query = request.IsDescending
            ? query.OrderByDescending(d => d.CreatedAt)
            : query.OrderBy(d => d.CreatedAt);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);

        var dtos = items.Select(d => IotDeviceMapper.ToDto(d, d.Site?.Name, d.TargetFirmwareRelease?.Version)).ToList();

        return new CommonResponse<PaginationResponse<IotDeviceDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<IotDeviceDto>
            {
                Items = dtos,
                TotalItems = total,
                PageNumber = page,
                PageSize = size
            }
        };
    }
}

public class GetIotDeviceByIdQueryHandler : IRequestHandler<GetIotDeviceByIdQuery, CommonResponse<IotDeviceDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    public GetIotDeviceByIdQueryHandler(IBatteryUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<IotDeviceDto>> Handle(GetIotDeviceByIdQuery request, CancellationToken ct)
    {
        var entity = await _unitOfWork.IotDevices.GetAllAsync()
            .Include(d => d.Site)
            .Include(d => d.TargetFirmwareRelease)
            .FirstOrDefaultAsync(d => d.Id == request.Id && !d.IsDeleted, ct);
        if (entity is null)
            return new CommonResponse<IotDeviceDto> { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy device." };

        return new CommonResponse<IotDeviceDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = IotDeviceMapper.ToDto(entity, entity.Site?.Name, entity.TargetFirmwareRelease?.Version)
        };
    }
}

public class GetIotDeviceByCodeQueryHandler : IRequestHandler<GetIotDeviceByCodeQuery, CommonResponse<IotDeviceDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    public GetIotDeviceByCodeQueryHandler(IBatteryUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<IotDeviceDto>> Handle(GetIotDeviceByCodeQuery request, CancellationToken ct)
    {
        // Chuẩn hoá giống lúc Create lưu (Trim().ToUpperInvariant()) — khớp unique index idx_iot_devices_device_code.
        var code = (request.DeviceCode ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0)
            return new CommonResponse<IotDeviceDto> { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy device." };

        var entity = await _unitOfWork.IotDevices.GetAllAsync()
            .Include(d => d.Site)
            .Include(d => d.TargetFirmwareRelease)
            .FirstOrDefaultAsync(d => d.DeviceCode == code && !d.IsDeleted, ct);
        if (entity is null)
            return new CommonResponse<IotDeviceDto> { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy device." };

        return new CommonResponse<IotDeviceDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = IotDeviceMapper.ToDto(entity, entity.Site?.Name, entity.TargetFirmwareRelease?.Version)
        };
    }
}
