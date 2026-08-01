using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryService.Infrastructure.BackgroundServices;

/// <summary>
/// Sprint IoT-2 #IoT2-33 (S7-BE-02) — chạy hằng ngày, scan calibration sắp expire trong 30 ngày
/// → tạo Alert (Severity Warning) cho Manager + log. Dedup: chỉ alert 1 lần/calibration trong 7 ngày.
/// </summary>
public class CalibrationExpiryNotificationBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private const int WithinDays = 30;
    private static readonly TimeSpan AlertDedupWindow = TimeSpan.FromDays(7);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CalibrationExpiryNotificationBackgroundService> _logger;

    public CalibrationExpiryNotificationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<CalibrationExpiryNotificationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Đợi 1 phút sau khi boot để các service khác sẵn sàng.
        try
        { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CalibrationExpiry scan failed");
            }

            try
            { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ScanOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IBatteryUnitOfWork>();

        var now = DateTime.UtcNow;
        var boundary = now.AddDays(WithinDays);
        var dedupSince = now.Subtract(AlertDedupWindow);

        var expiring = await uow.IotDeviceCalibrations.GetAllAsync()
            .Include(c => c.IotDevice)
            .Where(c => !c.IsDeleted && c.ExpiresAt != null && c.ExpiresAt > now && c.ExpiresAt <= boundary)
            .ToListAsync(ct);

        if (expiring.Count == 0)
            return;

        var alertCount = 0;
        foreach (var cal in expiring)
        {
            // Dedup: bỏ qua nếu trong 7 ngày qua đã có alert SohDegradation kèm BatteryAssetId này
            // — ở đây dùng SohDegradation là proxy; nếu cần riêng có thể mở rộng AnomalyType.
            // Note: domain chưa có "CalibrationExpiring" → dùng SohDegradation Warning + Notes embed channel.
            var assetIdForAlert = cal.BatteryAssetId
                ?? await uow.BatteryAssets.GetAllAsync()
                    .Where(a => !a.IsDeleted && a.SiteId == cal.IotDevice!.SiteId)
                    .Select(a => a.Id)
                    .FirstOrDefaultAsync(ct);

            if (assetIdForAlert == Guid.Empty)
                continue;

            var dup = await uow.Alerts.GetAllAsync()
                .AnyAsync(a => !a.IsDeleted
                               && a.BatteryAssetId == assetIdForAlert
                               && a.AnomalyType == AnomalyTypeEnum.SohDegradation
                               && a.DetectedAt >= dedupSince, ct);
            if (dup)
                continue;

            await uow.Alerts.AddAsync(new Alert
            {
                // Cùng lỗi với CrossSourceValidationService: `Alert.Id` không được EF tự sinh, bỏ
                // trống là mọi Alert trong vòng lặp đều mang Guid.Empty ⇒ AddAsync thứ hai ném
                // "another instance with the same key value for {'Id'} is already being tracked".
                // Job này quét NHIỀU calibration hết hạn một lượt nên gần như chắc chắn dính.
                Id = Guid.NewGuid(),
                BatteryAssetId = assetIdForAlert,
                SiteId = cal.IotDevice?.SiteId,
                AnomalyType = AnomalyTypeEnum.SohDegradation,
                Severity = AlertSeverityEnum.Warning,
                DetectedAt = now,
                Status = AlertStatusEnum.Open,
                DedupWindowEndUtc = now.AddDays(7)
            });
            alertCount++;
        }

        if (alertCount > 0)
        {
            await uow.SaveChangesAsync(ct);
            _logger.LogInformation("CalibrationExpiry: {Count} calibration sắp hết hạn → tạo {Alerts} alert", expiring.Count, alertCount);
        }
    }
}
