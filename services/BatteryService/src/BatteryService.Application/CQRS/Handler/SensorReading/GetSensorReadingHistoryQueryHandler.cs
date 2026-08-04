using BatteryService.Application.CQRS.Query.SensorReading;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.SensorReading;

public class GetSensorReadingHistoryQueryHandler : IRequestHandler<GetSensorReadingHistoryQuery, CommonResponse<SensorReadingHistoryResponseDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetSensorReadingHistoryQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<SensorReadingHistoryResponseDto>> Handle(GetSensorReadingHistoryQuery request, CancellationToken cancellationToken)
    {
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

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }
}
