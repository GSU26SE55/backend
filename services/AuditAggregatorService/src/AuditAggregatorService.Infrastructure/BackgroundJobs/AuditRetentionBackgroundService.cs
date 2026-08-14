using AuditAggregatorService.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AuditAggregatorService.Infrastructure.BackgroundJobs;

/// <summary>
/// Retention cho read-store (Sprint audit #AUDIT-41). Daily ~03:00 UTC: xóa row <c>audit_aggregate</c> cũ hơn
/// 6 tháng TRỪ severity Critical/Security (giữ vĩnh viễn — D15). Source tables retain 1 năm (per-service job).
/// </summary>
public class AuditRetentionBackgroundService : BackgroundService
{
    /// <summary>Giờ UTC chạy dọn dẹp hằng ngày (thấp điểm).</summary>
    protected virtual int ScheduledHourUtc => 3;

    /// <summary>
    /// GH-729 — thời gian chờ tới lần chạy kế tiếp.
    ///
    /// <para><b>Lỗi cũ:</b> dùng <c>PeriodicTimer(6h)</c> neo theo thời điểm process khởi động,
    /// rồi mới kiểm "có đang trong cửa sổ 03:00–04:59 không". Tick vì thế luôn rơi vào 4 mốc cố
    /// định cách nhau 6 giờ tính từ lúc start ⇒ chỉ trúng cửa sổ khi
    /// <c>(giờ khởi động mod 6h)</c> nằm trong [3h, 5h). Đo cụ thể: <b>67% mốc khởi động thì
    /// retention KHÔNG BAO GIỜ chạy</b> (vd start 00:30 → tick 06:30/12:30/18:30/00:30 mãi mãi),
    /// và DB phình vô hạn trong im lặng. Comment cũ ở đây khẳng định "6h đảm bảo trúng cửa sổ
    /// 1 lần/ngày" — sai.</para>
    ///
    /// <para><b>Cách sửa:</b> ngủ tới đúng mốc <see cref="ScheduledHourUtc"/> kế tiếp, không phụ
    /// thuộc lúc khởi động. Tính lại mỗi vòng nên không trôi lịch và tự đúng khi qua DST/đổi ngày.</para>
    ///
    /// <c>protected virtual</c> để test rút ngắn được — chờ tới 3 giờ sáng thì không test được.
    /// </summary>
    protected virtual TimeSpan DelayUntilNextRun(DateTime utcNow)
    {
        var todayRun = new DateTime(
            utcNow.Year, utcNow.Month, utcNow.Day, ScheduledHourUtc, 0, 0, DateTimeKind.Utc);

        // utcNow == todayRun (đúng boong) vẫn chạy hôm nay, không đẩy sang ngày mai.
        var next = utcNow <= todayRun ? todayRun : todayRun.AddDays(1);
        return next - utcNow;
    }

    /// <summary>
    /// Có phải cửa sổ được phép dọn dẹp không (mặc định 03:00–04:59 UTC, giờ thấp điểm).
    /// Sau khi <see cref="DelayUntilNextRun"/> ngủ đúng tới 03:00 thì đây chỉ còn là chốt an
    /// toàn (vd máy ngủ đông làm lệch giờ thức dậy).
    /// Tách ra <c>protected virtual</c> vì nếu không, test chỉ đúng khi tình cờ chạy lúc 3 giờ
    /// sáng UTC — một test phụ thuộc giờ chạy là test đỏ ngẫu nhiên.
    /// </summary>
    protected virtual bool IsWithinMaintenanceWindow(DateTime utcNow) => utcNow.Hour is >= 3 and <= 4;

    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(180); // ~6 tháng

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditRetentionBackgroundService> _logger;

    public AuditRetentionBackgroundService(IServiceScopeFactory scopeFactory, ILogger<AuditRetentionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AuditRetention started (window={Days}d, except Critical/Security, chạy hằng ngày {Hour:00}:00 UTC).",
            RetentionWindow.TotalDays, ScheduledHourUtc);

        while (!stoppingToken.IsCancellationRequested)
        {
            // GH-729 — ngủ tới đúng mốc kế tiếp thay vì tick theo chu kỳ neo vào lúc khởi động.
            try
            {
                var delay = DelayUntilNextRun(DateTime.UtcNow);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) { break; }

            if (stoppingToken.IsCancellationRequested)
                break;

            try
            {
                var now = DateTime.UtcNow;
                // Chốt an toàn: bình thường đã ngủ đúng tới giờ nên luôn đúng cửa sổ.
                if (!IsWithinMaintenanceWindow(now))
                    continue;

                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IAuditAggregatorUnitOfWork>();
                var cutoff = now - RetentionWindow;

                // Bulk delete row cũ hơn cutoff TRỪ severity Critical/Security (giữ vĩnh viễn — D15).
                var deleted = await uow.AuditAggregates.GetAllAsync()
                    .Where(x => x.OccurredAt < cutoff && x.Severity != "Critical" && x.Severity != "Security")
                    .ExecuteDeleteAsync(stoppingToken);
                if (deleted > 0)
                    _logger.LogInformation("AuditRetention xóa {Count} row cũ hơn {Cutoff:O}.", deleted, cutoff);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "AuditRetention tick failed."); }
        }
    }
}
