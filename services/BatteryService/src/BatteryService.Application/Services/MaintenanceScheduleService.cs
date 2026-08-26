using BatteryService.Application.Common.Models;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BatteryService.Application.Services;

public interface IMaintenanceScheduleService
{
    /// <summary>Ghi log cho mọi pin đã tới kỳ và dời lịch sang kỳ sau. Trả số kỳ đã ghi.</summary>
    Task<int> RecordDueCyclesAsync(DateTime nowUtc, CancellationToken ct);
}

/// <summary>
/// Nhật ký bảo trì định kỳ ở tầng tài sản: đến kỳ thì ghi một mốc theo dõi kèm sức khoẻ
/// pin tại thời điểm đó, rồi dời lịch sang kỳ kế tiếp.
/// </summary>
/// <remarks>
/// <para>
/// Nguồn sự thật là <c>BatteryAsset.NextMaintenanceDueAtUtc</c> — cột thật, có index.
/// Trước đây lịch được suy ngược mỗi tick từ ticket Closed gần nhất của pin
/// (<c>GroupBy(battery_asset_id)</c> trên toàn bảng tickets bên TicketService), nên: pin
/// chưa từng có ticket Closed thì không bao giờ vào lịch; mọi ticket đóng — kể cả khiếu
/// nại vặt — đều dời chu kỳ; và không thể trả lời "pin nào sắp tới hạn" nếu không quét
/// bảng ticket.
/// </para>
/// <para>
/// Service này KHÔNG tạo ticket và không phát sự kiện. Nó chỉ ghi nhật ký: mỗi kỳ một
/// dòng <see cref="MaintenanceCycle"/> kèm <c>SohPercentAtCompletion</c> — đặt các kỳ
/// cạnh nhau sẽ thấy đường suy giảm sức khoẻ pin qua từng chu kỳ.
/// </para>
/// </remarks>
public class MaintenanceScheduleService : IMaintenanceScheduleService
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IOptions<MaintenanceScheduleOptions> _options;
    private readonly ILogger<MaintenanceScheduleService> _logger;

    public MaintenanceScheduleService(
        IBatteryUnitOfWork unitOfWork,
        IOptions<MaintenanceScheduleOptions> options,
        ILogger<MaintenanceScheduleService> logger)
    {
        _unitOfWork = unitOfWork;
        _options = options;
        _logger = logger;
    }

    public async Task<int> RecordDueCyclesAsync(DateTime nowUtc, CancellationToken ct)
    {
        var options = _options.Value;

        // Chỉ pin đang hoạt động: pin Inactive/Decommissioned không cần theo dõi định kỳ.
        var due = await _unitOfWork.BatteryAssets.GetAllAsync()
            .Include(asset => asset.BatteryType)
            .Where(asset =>
                !asset.IsDeleted &&
                asset.Status == Domain.Enums.BatteryStatusEnum.Active &&
                asset.NextMaintenanceDueAtUtc <= nowUtc)
            .OrderBy(asset => asset.NextMaintenanceDueAtUtc)
            .Take(options.BatchSize)
            .ToListAsync(ct);

        if (due.Count == 0)
            return 0;

        var recorded = 0;
        foreach (var asset in due)
        {
            if (await RecordOneAsync(asset, nowUtc, ct))
                recorded++;
        }

        return recorded;
    }

    private async Task<bool> RecordOneAsync(BatteryAsset asset, DateTime nowUtc, CancellationToken ct)
    {
        var dueAtUtc = asset.NextMaintenanceDueAtUtc;
        var interval = asset.BatteryType?.MaintenanceIntervalMonths
            ?? _options.Value.DefaultCycleMonths;

        // Kỳ này trải từ mốc trước tới mốc hiện tại. Kỳ đầu tiên chưa có mốc trước nên
        // lùi về đúng một chu kỳ — không dùng InstallDate, vì pin có thể được lắp từ rất
        // lâu và khoảng đó sẽ không còn là "6 tháng qua" nữa.
        var periodStart = asset.LastMaintenanceAtUtc ?? dueAtUtc.AddMonths(-interval);
        var snapshot = await BuildSnapshotAsync(asset.Id, periodStart, dueAtUtc, ct);

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            await _unitOfWork.MaintenanceCycles.AddAsync(new MaintenanceCycle
            {
                Id = Guid.NewGuid(),
                BatteryAssetId = asset.Id,
                CycleNo = asset.MaintenanceCycleNo,
                DueAtUtc = dueAtUtc,
                RecordedAtUtc = nowUtc,
                SohPercentAtCycle = snapshot.SohPercent,
                AvgTemperatureCelsius = snapshot.AvgTemperature,
                MaxTemperatureCelsius = snapshot.MaxTemperature,
                MinVoltage = snapshot.MinVoltage,
                MaxVoltage = snapshot.MaxVoltage,
                CycleCountDelta = snapshot.CycleCountDelta,
                AlertCount = snapshot.AlertCount,
                CriticalAlertCount = snapshot.CriticalAlertCount,
                ReadingCount = snapshot.ReadingCount,
                CreatedAt = nowUtc
            });

            // Chu kỳ tính từ hạn kế hoạch, không phải từ lúc ghi: worker có thể chạy trễ
            // vài phút, nhưng mốc theo dõi thì phải đều đặn theo chu kỳ.
            asset.LastMaintenanceAtUtc = dueAtUtc;
            asset.NextMaintenanceDueAtUtc = dueAtUtc.AddMonths(interval);
            asset.MaintenanceCycleNo++;
            _unitOfWork.BatteryAssets.UpdateAsync(asset);

            await _unitOfWork.CommitTransactionAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            // Va unique index (asset, cycle_no) — một replica khác đã ghi kỳ này trước.
            await _unitOfWork.RollbackTransactionAsync();
            _logger.LogInformation(
                "Maintenance cycle {CycleNo} for battery {AssetId} already recorded.",
                asset.MaintenanceCycleNo, asset.Id);
            return false;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }

    private sealed record CycleSnapshot(
        decimal? SohPercent,
        decimal? AvgTemperature,
        decimal? MaxTemperature,
        decimal? MinVoltage,
        decimal? MaxVoltage,
        int? CycleCountDelta,
        int? AlertCount,
        int? CriticalAlertCount,
        int ReadingCount);

    /// <summary>
    /// Tổng hợp tình trạng pin trong khoảng [<paramref name="fromUtc"/>, <paramref name="toUtc"/>).
    /// </summary>
    /// <remarks>
    /// Chụp một lần lúc ghi mốc thay vì tính lại khi đọc: <c>sensor_readings</c> là
    /// hypertable có chính sách lưu trữ nên dữ liệu cũ sẽ bị dọn, và gộp 6 tháng mỗi lần
    /// mở trang là quá đắt. Pin mất kết nối cả kỳ thì trả về bản ghi rỗng — không chặn
    /// việc ghi mốc.
    /// </remarks>
    private async Task<CycleSnapshot> BuildSnapshotAsync(
        Guid assetId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct)
    {
        var readings = _unitOfWork.SensorReadings.GetAllAsync()
            .AsNoTracking()
            .Where(reading =>
                reading.BatteryAssetId == assetId &&
                reading.Time >= fromUtc &&
                reading.Time < toUtc);

        var stats = await readings
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Count = group.Count(),
                AvgTemp = (decimal?)group.Average(reading => reading.Temperature),
                MaxTemp = (decimal?)group.Max(reading => reading.Temperature),
                MinVolt = (decimal?)group.Min(reading => reading.Voltage),
                MaxVolt = (decimal?)group.Max(reading => reading.Voltage),
                MinCycles = group.Min(reading => reading.CycleCount),
                MaxCycles = group.Max(reading => reading.CycleCount)
            })
            .FirstOrDefaultAsync(ct);

        if (stats is null || stats.Count == 0)
            return new CycleSnapshot(null, null, null, null, null, null, 0, 0, 0);

        // SoH lấy từ bản ghi cảm biến MỚI NHẤT trong kỳ, không phải trung bình: đây là
        // sức khoẻ pin tại mốc, không phải mức bình quân suốt kỳ.
        var sohPercent = await readings
            .Where(reading => reading.SohPercent != null)
            .OrderByDescending(reading => reading.Time)
            .Select(reading => reading.SohPercent)
            .FirstOrDefaultAsync(ct);

        var alerts = _unitOfWork.Alerts.GetAllAsync()
            .AsNoTracking()
            .Where(alert =>
                alert.BatteryAssetId == assetId &&
                !alert.IsDeleted &&
                alert.DetectedAt >= fromUtc &&
                alert.DetectedAt < toUtc);

        var alertCount = await alerts.CountAsync(ct);
        var criticalCount = await alerts
            .CountAsync(alert => alert.Severity == Domain.Enums.AlertSeverityEnum.Critical, ct);

        // cycle_count là bộ đếm cộng dồn của BMS. Chênh lệch đầu–cuối kỳ cho biết pin đã
        // sạc/xả bao nhiêu lần. Bỏ qua nếu BMS không báo, hoặc nếu bộ đếm bị reset
        // (thay BMS) khiến hiệu ra số âm.
        int? cycleDelta = stats.MinCycles.HasValue && stats.MaxCycles.HasValue
            ? stats.MaxCycles.Value - stats.MinCycles.Value
            : null;
        if (cycleDelta < 0)
            cycleDelta = null;

        return new CycleSnapshot(
            sohPercent,
            stats.AvgTemp is null ? null : Math.Round(stats.AvgTemp.Value, 2),
            stats.MaxTemp,
            stats.MinVolt,
            stats.MaxVolt,
            cycleDelta,
            alertCount,
            criticalCount,
            stats.Count);
    }
}
