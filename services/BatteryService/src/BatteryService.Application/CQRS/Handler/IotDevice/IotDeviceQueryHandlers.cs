using BatteryService.Application.CQRS.Query.IotDevice;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;

namespace BatteryService.Application.CQRS.Handler.IotDevice;

public class GetIotDevicesQueryHandler : IRequestHandler<GetIotDevicesQuery, CommonResponse<PaginationResponse<IotDeviceDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    public GetIotDevicesQueryHandler(IBatteryUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<PaginationResponse<IotDeviceDto>>> Handle(GetIotDevicesQuery request, CancellationToken ct)
    {
        // PaginationRequest đã clamp: PageNumber >= 1, PageSize trong [1, 100]
        var page = request.PageNumber;
        var size = request.PageSize;

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

        // SortDir (mới) thắng nếu có; nếu không dùng IsDescending (legacy) để giữ tương thích ngược.
        var descending = string.IsNullOrWhiteSpace(request.SortDir)
            ? request.IsDescending
            : SortHelper.IsDescending(request.SortDir);

        // Whitelist: deviceCode | displayName | siteName | status | currentFirmwareVersion | lastSeenAt | createdAt (default).
        var ordered = (request.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "devicecode" => descending ? query.OrderByDescending(d => d.DeviceCode) : query.OrderBy(d => d.DeviceCode),
            "displayname" => descending ? query.OrderByDescending(d => d.DisplayName) : query.OrderBy(d => d.DisplayName),
            "sitename" => descending ? query.OrderByDescending(d => d.Site != null ? d.Site.Name : null) : query.OrderBy(d => d.Site != null ? d.Site.Name : null),
            "status" => descending ? query.OrderByDescending(d => d.Status) : query.OrderBy(d => d.Status),
            "currentfirmwareversion" => descending ? query.OrderByDescending(d => d.CurrentFirmwareVersion) : query.OrderBy(d => d.CurrentFirmwareVersion),
            "lastseenat" => descending ? query.OrderByDescending(d => d.LastSeenAt) : query.OrderBy(d => d.LastSeenAt),
            _ => descending ? query.OrderByDescending(d => d.CreatedAt) : query.OrderBy(d => d.CreatedAt),
        };
        query = ordered.ThenBy(d => d.Id); // tie-breaker cố định — pagination ổn định

        // IotDeviceMapper.ToDto là method call (còn nhận thêm navigation Site/TargetFirmwareRelease đã
        // Include sẵn) → không dịch sang SQL được, nên phân trang trên entity rồi mới đổi kiểu.
        var paged = await query.ToPagedEntityListAsync(page, size, ct);

        return new CommonResponse<PaginationResponse<IotDeviceDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = paged.Map(d => IotDeviceMapper.ToDto(d, d.Site?.Name, d.TargetFirmwareRelease?.Version))
        };
    }
}

public class GetIotDeviceByIdQueryHandler : IRequestHandler<GetIotDeviceByIdQuery, CommonResponse<IotDeviceDetailDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IMqttBrokerEndpointProvider _broker;

    public GetIotDeviceByIdQueryHandler(IBatteryUnitOfWork unitOfWork, IMqttBrokerEndpointProvider broker)
    {
        _unitOfWork = unitOfWork;
        _broker = broker;
    }

    public async Task<CommonResponse<IotDeviceDetailDto>> Handle(GetIotDeviceByIdQuery request, CancellationToken ct)
    {
        var entity = await _unitOfWork.IotDevices.GetAllAsync()
            .Include(d => d.Site)
            .Include(d => d.TargetFirmwareRelease)
            .FirstOrDefaultAsync(d => d.Id == request.Id && !d.IsDeleted, ct);
        if (entity is null)
            return new CommonResponse<IotDeviceDetailDto> { IsSuccess = false, StatusCode = 404, Message = "Device not found." };

        return new CommonResponse<IotDeviceDetailDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            // IOT3-71 — điểm kết nối MQTT lấy từ cấu hình đang chạy, KHÔNG lưu trong DB:
            // đổi broker là mọi thiết bị phải thấy địa chỉ mới ngay, không phải chờ ai đó nhớ
            // chạy một câu UPDATE.
            Data = IotDeviceMapper.ToDetailDto(
                entity, entity.Site?.Name, entity.TargetFirmwareRelease?.Version,
                _broker.Resolve(entity.DeviceCode))
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
            return new CommonResponse<IotDeviceDto> { IsSuccess = false, StatusCode = 404, Message = "Device not found." };

        var entity = await _unitOfWork.IotDevices.GetAllAsync()
            .Include(d => d.Site)
            .Include(d => d.TargetFirmwareRelease)
            .FirstOrDefaultAsync(d => d.DeviceCode == code && !d.IsDeleted, ct);
        if (entity is null)
            return new CommonResponse<IotDeviceDto> { IsSuccess = false, StatusCode = 404, Message = "Device not found." };

        return new CommonResponse<IotDeviceDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = IotDeviceMapper.ToDto(entity, entity.Site?.Name, entity.TargetFirmwareRelease?.Version)
        };
    }
}
