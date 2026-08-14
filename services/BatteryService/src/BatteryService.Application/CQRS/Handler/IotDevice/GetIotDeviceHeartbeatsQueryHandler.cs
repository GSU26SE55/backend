using BatteryService.Application.CQRS.Query.IotDevice;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.IotDevice;

/// <summary>IOT3-58 — đọc lịch sử heartbeat, phân trang theo con trỏ.</summary>
public class GetIotDeviceHeartbeatsQueryHandler
    : IRequestHandler<GetIotDeviceHeartbeatsQuery, CommonResponse<IotDeviceHeartbeatListDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetIotDeviceHeartbeatsQueryHandler(IBatteryUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<IotDeviceHeartbeatListDto>> Handle(
        GetIotDeviceHeartbeatsQuery request, CancellationToken ct)
    {
        // Thiết bị không tồn tại phải ra 404 chứ không phải "danh sách rỗng": rỗng có nghĩa
        // "thiết bị này chưa từng gửi heartbeat", một kết luận hoàn toàn khác và sẽ khiến người
        // trực đi tìm nhầm chỗ.
        var deviceExists = await _unitOfWork.IotDevices.GetAllAsync()
            .AnyAsync(d => d.Id == request.DeviceId && !d.IsDeleted, ct);
        if (!deviceExists)
        {
            return new CommonResponse<IotDeviceHeartbeatListDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy device."
            };
        }

        // `IotDeviceHeartbeat` KHÔNG kế thừa AuditableEntity (bảng append-only), nên không có
        // `IsDeleted` để lọc — khác mọi query khác của dự án. Đây là chủ ý, không phải bỏ sót.
        var query = _unitOfWork.IotDeviceHeartbeats.GetAllAsync()
            .Where(h => h.IotDeviceId == request.DeviceId);

        if (request.From.HasValue)
        {
            var from = ToUtc(request.From.Value);
            query = query.Where(h => h.Time >= from);
        }
        if (request.To.HasValue)
        {
            var to = ToUtc(request.To.Value);
            query = query.Where(h => h.Time <= to);
        }

        // Mới trước ⇒ trang kế lấy bản ghi CŨ HƠN con trỏ.
        if (request.Cursor.HasValue)
        {
            var cursor = ToUtc(request.Cursor.Value);
            query = query.Where(h => h.Time < cursor);
        }

        // Lấy dư 1 bản ghi để biết còn trang sau hay không, thay vì chạy thêm một câu COUNT.
        var page = await query
            .OrderByDescending(h => h.Time)
            .Take(request.Limit + 1)
            .Select(h => new IotDeviceHeartbeatDto
            {
                Time = h.Time,
                FirmwareVersion = h.FirmwareVersion,
                RssiDbm = h.RssiDbm,
                FreeMemoryPercent = h.FreeMemoryPercent,
                UptimeSeconds = h.UptimeSeconds,
                QueuedReadingCount = h.QueuedReadingCount,
                DeviceTimestamp = h.DeviceTimestamp,
                ClockSkewSeconds = h.ClockSkewSeconds
            })
            .ToListAsync(ct);

        var hasMore = page.Count > request.Limit;
        var items = hasMore ? page.Take(request.Limit).ToList() : page;

        return new CommonResponse<IotDeviceHeartbeatListDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new IotDeviceHeartbeatListDto
            {
                Items = items,
                NextCursor = hasMore ? items[^1].Time : null,
                HasMore = hasMore,
                TotalCount = null   // luôn null cho chuỗi thời gian — xem DTO
            }
        };
    }

    private static DateTime ToUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
