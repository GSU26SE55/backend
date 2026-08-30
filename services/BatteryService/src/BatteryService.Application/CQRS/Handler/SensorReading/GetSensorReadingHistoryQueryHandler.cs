using BatteryService.Application.Anomaly;
using BatteryService.Application.CQRS.Query.SensorReading;
using BatteryService.Application.DTOs;
using BatteryService.Application.Helpers;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.SensorReading;

public class GetSensorReadingHistoryQueryHandler : IRequestHandler<GetSensorReadingHistoryQuery, CommonResponse<SensorReadingHistoryResponseDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IBatteryCurrentUserService _currentUserService;

    public GetSensorReadingHistoryQueryHandler(IBatteryUnitOfWork unitOfWork, IBatteryCurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<SensorReadingHistoryResponseDto>> Handle(GetSensorReadingHistoryQuery request, CancellationToken cancellationToken)
    {
        // GH-722 — telemetry thuộc tenant qua asset; Customer chỉ đọc được asset của mình.
        var scope = BatteryTenantScopeHelper.Resolve(_currentUserService.UserId, _currentUserService.Roles);
        if (scope.IsDenied)
        {
            return new CommonResponse<SensorReadingHistoryResponseDto>
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Could not identify the current user."
            };
        }

        // 404 thay vì 403: không tiết lộ rằng asset của tenant khác có tồn tại.
        if (!await BatteryTenantAccessGuard.CanAccessAssetAsync(_unitOfWork, request.BatteryAssetId, scope, cancellationToken))
        {
            return new CommonResponse<SensorReadingHistoryResponseDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Battery asset not found."
            };
        }

        var query = _unitOfWork.SensorReadings
            .GetAllAsync()
            .AsNoTracking()
            .Where(reading => reading.BatteryAssetId == request.BatteryAssetId);

        if (request.From.HasValue)
        {
            var from = ToUtc(request.From.Value);
            query = query.Where(reading => reading.Time >= from);
        }

        if (request.To.HasValue)
        {
            var to = ToUtc(request.To.Value);
            query = query.Where(reading => reading.Time <= to);
        }

        var limit = Math.Clamp(request.Limit, 1, GetSensorReadingHistoryQuery.MaxLimit);

        // Projection dùng chung cho cả 2 path để không lệch shape.
        System.Linq.Expressions.Expression<Func<Domain.Entities.SensorReading, SensorReadingDto>> toDto = reading => new SensorReadingDto
        {
            Time = reading.Time,
            BatteryAssetId = reading.BatteryAssetId.ToString(),
            Voltage = reading.Voltage,
            Current = reading.Current,
            Temperature = reading.Temperature,
            SocPercent = reading.SocPercent,
            CycleCount = reading.CycleCount,
            SourceDeviceId = reading.SourceDeviceId
        };

        var valueSort = request.NormalizedValueSort();
        if (valueSort != null)
        {
            // Hướng B — sort theo cột value trong [from, to] (đã validate bắt buộc), KHÔNG dùng cursor.
            // Lọc primary source: tránh 3 dòng/tick (redundant temp=0 / external voltage=0) làm sort rác (conflict A).
            query = query.Where(reading => reading.SensorSourceCode == "primary"
                || reading.SensorSourceCode == null
                || reading.SensorSourceCode == "");

            var descending = SortHelper.IsDescending(request.SortDir);
            var valueOrdered = valueSort switch
            {
                "voltage" => descending ? query.OrderByDescending(r => r.Voltage) : query.OrderBy(r => r.Voltage),
                "current" => descending ? query.OrderByDescending(r => r.Current) : query.OrderBy(r => r.Current),
                "temperature" => descending ? query.OrderByDescending(r => r.Temperature) : query.OrderBy(r => r.Temperature),
                _ => descending ? query.OrderByDescending(r => r.SocPercent) : query.OrderBy(r => r.SocPercent),
            };

            var valueItems = await valueOrdered
                .ThenByDescending(r => r.Time) // tie-breaker cố định
                .Take(limit)
                .Select(toDto)
                .ToListAsync(cancellationToken);

            await AttachAnomaliesAsync(request.BatteryAssetId, valueItems, cancellationToken);

            return new CommonResponse<SensorReadingHistoryResponseDto>
            {
                IsSuccess = true,
                StatusCode = 200,
                Data = new SensorReadingHistoryResponseDto
                {
                    Items = valueItems,
                    NextCursor = null, // Hướng B: sort theo value → FE tắt "Tải thêm"
                    HasMore = false
                }
            };
        }

        // Sort theo time (mặc định) — cursor path. Giữ nguyên hành vi cũ (desc); hỗ trợ thêm asc.
        var timeAscending = string.Equals(request.SortDir?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);

        if (request.Cursor.HasValue)
        {
            var cursor = ToUtc(request.Cursor.Value);
            query = timeAscending
                ? query.Where(reading => reading.Time > cursor)
                : query.Where(reading => reading.Time < cursor);
        }

        var page = await (timeAscending
                ? query.OrderBy(reading => reading.Time)
                : query.OrderByDescending(reading => reading.Time))
            .Take(limit + 1)
            .Select(toDto)
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > limit;
        var items = hasMore ? page.Take(limit).ToList() : page;

        await AttachAnomaliesAsync(request.BatteryAssetId, items, cancellationToken);

        return new CommonResponse<SensorReadingHistoryResponseDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new SensorReadingHistoryResponseDto
            {
                Items = items,
                NextCursor = hasMore ? items[^1].Time : null,
                HasMore = hasMore
            }
        };
    }

    /// <summary>
    /// Chấm anomaly cho từng dòng bằng ĐÚNG luật BE (<see cref="AnomalyRules"/>) thay vì để FE
    /// tự so ngưỡng. Chạy SAU khi materialize: Detect là C# thuần, không dịch được sang SQL.
    ///
    /// Không có ThresholdConfig cho loại pin thì để danh sách rỗng — nghĩa là "chưa cấu hình
    /// ngưỡng", KHÔNG phải "không vi phạm"; bịa ngưỡng mặc định ở đây sẽ tạo cảnh báo giả.
    /// </summary>
    private async Task AttachAnomaliesAsync(
        Guid batteryAssetId,
        IReadOnlyList<SensorReadingDto> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return;

        var batteryTypeId = await _unitOfWork.BatteryAssets.GetAllAsync()
            .AsNoTracking()
            .Where(asset => asset.Id == batteryAssetId && !asset.IsDeleted)
            .Select(asset => (Guid?)asset.BatteryTypeId)
            .FirstOrDefaultAsync(cancellationToken);
        if (batteryTypeId is null)
            return;

        var threshold = await _unitOfWork.ThresholdConfigs.GetAllAsync()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.BatteryTypeId == batteryTypeId.Value && !t.IsDeleted, cancellationToken);
        if (threshold is null)
            return;

        foreach (var item in items)
        {
            // Detect nhận entity SensorReading — dựng lại từ DTO để dùng CHUNG một bộ luật với
            // luồng phát hiện thật, thay vì chép lại điều kiện ở đây rồi lệch nhau về sau.
            var reading = new Domain.Entities.SensorReading
            {
                BatteryAssetId = batteryAssetId,
                Time = item.Time,
                Voltage = item.Voltage,
                Current = item.Current,
                Temperature = item.Temperature,
                SocPercent = item.SocPercent,
                CycleCount = item.CycleCount
            };

            item.Anomalies = AnomalyRules.Detect(reading, threshold)
                .Select(a => new SensorReadingAnomalyDto
                {
                    Type = a.Type,
                    Severity = a.Severity,
                    ThresholdValue = a.ThresholdValue,
                    ActualValue = a.ActualValue,
                    Unit = a.Unit
                })
                .ToList();
        }
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }
}
