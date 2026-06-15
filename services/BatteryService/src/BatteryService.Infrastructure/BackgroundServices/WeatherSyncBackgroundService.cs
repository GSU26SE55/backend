using BatteryService.Application.Common.Models;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BatteryService.Infrastructure.BackgroundServices;

/// <summary>
/// Sprint 5B #90 — periodic poll Open-Meteo cho mọi Site có lat/lon.
/// Interval: 15 phút (cấu hình <c>WeatherSyncOptions.IntervalMinutes</c>).
/// Dedup window: 10 phút — không insert nếu đã có row trong khoảng đó.
/// </summary>
public class WeatherSyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WeatherSyncOptions _options;
    private readonly ILogger<WeatherSyncBackgroundService> _logger;

    public WeatherSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<WeatherSyncOptions> options,
        ILogger<WeatherSyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("WeatherSyncBackgroundService disabled by config.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));
        _logger.LogInformation("WeatherSync started (interval={Mins}m, dedup={Dedup}m)",
            _options.IntervalMinutes, _options.DedupWindowMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WeatherSync tick failed");
            }

            try
            { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { /* shutdown */ }
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IBatteryUnitOfWork>();
        var client = scope.ServiceProvider.GetRequiredService<IOpenMeteoClient>();

        var sites = await uow.Sites.GetAllAsync()
            .Where(s => !s.IsDeleted && s.Latitude != null && s.Longitude != null)
            .Select(s => new { s.Id, s.Latitude, s.Longitude })
            .ToListAsync(cancellationToken);

        if (sites.Count == 0)
            return;

        var dedupCutoff = DateTime.UtcNow.AddMinutes(-_options.DedupWindowMinutes);
        var siteIds = sites.Select(s => s.Id).ToList();

        var recentByeSite = await uow.AmbientReadings.GetAllAsync()
            .Where(r => siteIds.Contains(r.SiteId) && r.Time >= dedupCutoff)
            .Select(r => r.SiteId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var skipSet = new HashSet<Guid>(recentByeSite);
        int fetched = 0, inserted = 0;

        foreach (var site in sites)
        {
            if (skipSet.Contains(site.Id))
                continue;

            var sample = await client.GetCurrentAsync(site.Latitude!.Value, site.Longitude!.Value, cancellationToken);
            fetched++;
            if (sample is null)
                continue;

            await uow.AmbientReadings.AddAsync(new AmbientReading
            {
                Time = sample.SampleTimeUtc,
                SiteId = site.Id,
                AmbientTemperature = sample.TemperatureCelsius,
                Humidity = sample.Humidity,
                Source = BatteryService.Domain.Enums.AmbientReadingSourceEnum.WeatherApi,
                SourceDeviceId = "openmeteo"
            });
            inserted++;
        }

        if (inserted > 0)
            await uow.SaveChangesAsync(cancellationToken);

        if (fetched > 0)
            _logger.LogInformation("WeatherSync: fetched={Fetched}, inserted={Inserted}, skippedDedup={Skipped}",
                fetched, inserted, sites.Count - fetched);
    }
}
