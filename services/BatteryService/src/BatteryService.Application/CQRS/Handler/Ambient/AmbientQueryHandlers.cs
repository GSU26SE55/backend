using BatteryService.Application.CQRS.Query.Ambient;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;

namespace BatteryService.Application.CQRS.Handler.Ambient;

/// <summary>
/// Gộp các bản ghi rời của MỘT chu kỳ đọc thành một dòng hiển thị.
/// </summary>
/// <remarks>
/// Ba cảm biến ambient chạy độc lập nên mỗi cái ghi một hàng riêng, chỉ mang đúng field của mình.
/// Chúng cùng tick trong một vòng <c>loop()</c> nhưng KHÔNG cùng giây: DS18B20 chuyển đổi 12-bit
/// chặn 750 ms, nên nhiệt độ thường rơi sau khí gas khoảng 1 giây.
/// <para>
/// Cố ý KHÔNG cắt theo mốc thời gian cố định (phút, hay bội của chu kỳ gửi): một chu kỳ nằm vắt qua
/// ranh giới sẽ bị xé làm hai dòng nửa vời, và mỗi lần đổi chu kỳ gửi bên firmware lại phải nhớ sửa
/// hằng số bên này cho khớp. Thay vào đó gom theo CỬA SỔ TRƯỢT tính từ bản ghi mới nhất của cụm —
/// tự đúng với mọi chu kỳ gửi, miễn là chu kỳ đó dài hơn <see cref="CycleWindow"/>.
/// </para>
/// </remarks>
internal static class AmbientCycleMerger
{
    /// <summary>
    /// Bề rộng một chu kỳ đọc. Phải lớn hơn độ lệch giữa các cảm biến trong cùng vòng (~1–2 s) và
    /// nhỏ hơn hẳn chu kỳ gửi (hiện 15 s) để hai vòng liên tiếp không bị dính làm một.
    /// </summary>
    private static readonly TimeSpan CycleWindow = TimeSpan.FromSeconds(5);

    /// <param name="rowsNewestFirst">Bản ghi đã sắp xếp giảm dần theo <c>Time</c>.</param>
    public static List<AmbientReadingDto> Merge(IReadOnlyList<AmbientReading> rowsNewestFirst)
    {
        var cycles = new List<List<AmbientReading>>();
        foreach (var r in rowsNewestFirst)
        {
            // So với bản ghi MỚI NHẤT của cụm (không phải bản liền trước) — nếu so với bản liền trước
            // thì một chuỗi đọc dày sẽ nối dây chuyền vô hạn và nuốt cả giờ dữ liệu vào một dòng.
            var current = cycles.Count > 0 ? cycles[^1] : null;
            if (current is not null && current[0].Time - r.Time <= CycleWindow)
                current.Add(r);
            else
                cycles.Add(new List<AmbientReading> { r });
        }

        return cycles.Select(c => new AmbientReadingDto
        {
            Time = c[0].Time,
            SiteId = c[0].SiteId.ToString(),
            AmbientTemperature = c.Select(r => r.AmbientTemperature).FirstOrDefault(v => v.HasValue),
            Humidity = c.Select(r => r.Humidity).FirstOrDefault(v => v.HasValue),
            SolarIrradiance = c.Select(r => r.SolarIrradiance).FirstOrDefault(v => v.HasValue),
            GasConcentration = c.Select(r => r.GasConcentration).FirstOrDefault(v => v.HasValue),
            WaterLeakDetected = c.Select(r => r.WaterLeakDetected).FirstOrDefault(v => v.HasValue),
            Source = c[0].Source,
            SourceDeviceId = c[0].SourceDeviceId
        }).ToList();
    }
}

public class GetAmbientReadingHistoryQueryHandler
    : IRequestHandler<GetAmbientReadingHistoryQuery, AmbientReadingListResponse>
{
    private readonly IBatteryUnitOfWork _uow;
    public GetAmbientReadingHistoryQueryHandler(IBatteryUnitOfWork uow) { _uow = uow; }

    public async Task<AmbientReadingListResponse> Handle(GetAmbientReadingHistoryQuery request, CancellationToken cancellationToken)
    {
        var pageSize = request.PageSize is < 1 or > 1000 ? 100 : request.PageSize;
        var pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;

        var query = _uow.AmbientReadings.GetAllAsync().Where(r => r.SiteId == request.SiteId);
        if (request.From.HasValue)
            query = query.Where(r => r.Time >= request.From.Value);
        if (request.To.HasValue)
            query = query.Where(r => r.Time <= request.To.Value);

        // Gas/nhiệt độ/nước POST độc lập nên mỗi hàng DB chỉ có 1 field khác null; phải gộp một chu kỳ
        // đọc thành 1 dòng hiển thị. EF không dịch nổi "first non-null theo field" sang SQL GroupBy
        // nên gộp bằng LINQ-to-Objects.
        var rows = await query.OrderByDescending(r => r.Time).ToListAsync(cancellationToken);
        var merged = AmbientCycleMerger.Merge(rows);

        var page = new PaginationResponse<AmbientReadingDto>
        {
            Items = merged.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(),
            TotalItems = merged.Count,
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        return new AmbientReadingListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page
        };
    }
}

public class GetLatestAmbientReadingQueryHandler
    : IRequestHandler<GetLatestAmbientReadingQuery, AmbientReadingLatestResponse>
{
    private readonly IBatteryUnitOfWork _uow;
    public GetLatestAmbientReadingQueryHandler(IBatteryUnitOfWork uow) { _uow = uow; }

    public async Task<AmbientReadingLatestResponse> Handle(GetLatestAmbientReadingQuery request, CancellationToken cancellationToken)
    {
        var row = await _uow.AmbientReadings.GetAllAsync()
            .Where(r => r.SiteId == request.SiteId)
            .OrderByDescending(r => r.Time)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return new AmbientReadingLatestResponse { IsSuccess = false, StatusCode = 404, Message = "No reading found." };

        // "Latest" phải đại diện cho cả 3 cảm biến độc lập, nên lấy trọn chu kỳ đọc mới nhất chứ không
        // chỉ mỗi hàng cuối (hàng đó chỉ mang đúng 1 field). Dùng chung quy tắc gộp với bảng lịch sử.
        var windowStart = row.Time.AddSeconds(-10);
        var lastCycleRows = await _uow.AmbientReadings.GetAllAsync()
            .Where(r => r.SiteId == request.SiteId && r.Time >= windowStart && r.Time <= row.Time)
            .OrderByDescending(r => r.Time)
            .ToListAsync(cancellationToken);

        return new AmbientReadingLatestResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = AmbientCycleMerger.Merge(lastCycleRows).First()
        };
    }
}
