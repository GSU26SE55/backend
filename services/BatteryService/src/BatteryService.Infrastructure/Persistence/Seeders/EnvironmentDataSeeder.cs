using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BatteryService.Infrastructure.Persistence.Seeders;

/// <summary>
/// Sprint 5B — seed data cho ambient/environmental modules:
/// <list type="bullet">
///   <item><description><see cref="AmbientThresholdConfig"/> mặc định per-site.</description></item>
///   <item><description><see cref="AmbientReading"/> 1h gần nhất (12 reading mỗi 5 phút) cho mỗi site.</description></item>
///   <item><description><see cref="EnvironmentalIncident"/> kịch bản Open + Resolved để demo lifecycle.</description></item>
///   <item><description><see cref="NoiseBreachEvent"/> mẫu để kiểm thử frequency-based promotion logic (B1 §152).</description></item>
/// </list>
/// Tất cả ID tự sinh runtime bằng <c>Guid.NewGuid()</c>. Idempotent — không seed lại khi data đã tồn tại.
/// </summary>
public class EnvironmentDataSeeder
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<EnvironmentDataSeeder> _logger;

    public EnvironmentDataSeeder(ApplicationDbContext dbContext, ILogger<EnvironmentDataSeeder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var sites = await _dbContext.Sites
            .Where(s => !s.IsDeleted)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
        if (sites.Count == 0)
        {
            _logger.LogInformation("EnvironmentDataSeeder: no sites available, skipping.");
            return;
        }

        await SeedAmbientThresholdsAsync(sites, cancellationToken);
        await SeedAmbientReadingsAsync(sites, cancellationToken);
        await SeedEnvironmentalIncidentsAsync(sites, cancellationToken);
        await SeedNoiseBreachEventsAsync(cancellationToken);
        await SeedAlertsAsync(cancellationToken);
    }

    private async Task SeedAlertsAsync(CancellationToken cancellationToken)
    {
        var hasAlerts = await _dbContext.Alerts.AnyAsync(cancellationToken);
        if (hasAlerts)
            return;

        var assets = await _dbContext.BatteryAssets
            .OrderBy(a => a.SerialNumber)
            .Take(4)
            .ToListAsync(cancellationToken);
        if (assets.Count < 4)
            return;

        var now = DateTime.UtcNow;
        var alerts = new List<Alert>
        {
            // Asset 2 — Overheat Critical, đang Open
            new Alert
            {
                Id = Guid.NewGuid(),
                BatteryAssetId = assets[1].Id,
                SiteId = assets[1].SiteId,
                AnomalyType = AnomalyTypeEnum.Overheat,
                Severity = AlertSeverityEnum.Critical,
                ThresholdValue = 60m,
                ActualValue = 72m,
                Unit = "°C",
                DetectedAt = now.AddMinutes(-30),
                Status = AlertStatusEnum.Open,
                DedupWindowEndUtc = now.AddMinutes(30),
                CreatedAt = now.AddMinutes(-30)
            },
            // Asset 3 — LowSoc Critical, Acknowledged
            new Alert
            {
                Id = Guid.NewGuid(),
                BatteryAssetId = assets[2].Id,
                SiteId = assets[2].SiteId,
                AnomalyType = AnomalyTypeEnum.LowSoc,
                Severity = AlertSeverityEnum.Critical,
                ThresholdValue = 10m,
                ActualValue = 4.7m,
                Unit = "%",
                DetectedAt = now.AddMinutes(-20),
                Status = AlertStatusEnum.Acknowledged,
                AcknowledgedAt = now.AddMinutes(-10),
                AcknowledgedByUserId = Guid.NewGuid(),
                DedupWindowEndUtc = now.AddMinutes(40),
                CreatedAt = now.AddMinutes(-20)
            },
            // Asset 4 — SohDegradation Critical, Resolved
            new Alert
            {
                Id = Guid.NewGuid(),
                BatteryAssetId = assets[3].Id,
                SiteId = assets[3].SiteId,
                AnomalyType = AnomalyTypeEnum.SohDegradation,
                Severity = AlertSeverityEnum.Critical,
                ThresholdValue = 75m,
                ActualValue = 72m,
                Unit = "%",
                DetectedAt = now.AddHours(-3),
                Status = AlertStatusEnum.Resolved,
                AcknowledgedAt = now.AddHours(-2),
                AcknowledgedByUserId = Guid.NewGuid(),
                ResolvedAt = now.AddHours(-1),
                DedupWindowEndUtc = now.AddHours(-1),
                CreatedAt = now.AddHours(-3)
            },
            // Asset 1 — Info Warning, Open (demo non-critical)
            new Alert
            {
                Id = Guid.NewGuid(),
                BatteryAssetId = assets[0].Id,
                SiteId = assets[0].SiteId,
                AnomalyType = AnomalyTypeEnum.AbnormalCharging,
                Severity = AlertSeverityEnum.Warning,
                ThresholdValue = 5m,
                ActualValue = 6.2m,
                Unit = "A",
                DetectedAt = now.AddMinutes(-5),
                Status = AlertStatusEnum.Open,
                DedupWindowEndUtc = now.AddMinutes(55),
                CreatedAt = now.AddMinutes(-5)
            }
        };

        _dbContext.Alerts.AddRange(alerts);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} Alert rows.", alerts.Count);
    }

    private async Task SeedAmbientThresholdsAsync(List<Site> sites, CancellationToken cancellationToken)
    {
        var configuredSiteIds = await _dbContext.AmbientThresholdConfigs
            .Where(c => !c.IsDeleted)
            .Select(c => c.SiteId)
            .ToListAsync(cancellationToken);

        var toAdd = sites
            .Where(s => !configuredSiteIds.Contains(s.Id))
            .Select(s => new AmbientThresholdConfig
            {
                Id = Guid.NewGuid(),
                SiteId = s.Id,
                HighAmbientTempWarning = 38m,
                HighAmbientTempCritical = 42m,
                HighHumidityWarning = 80m,
                HighHumidityCritical = 90m,
                ComboTempThreshold = 35m,
                ComboHumidityThreshold = 75m,
                Enabled = true,
                CreatedAt = SeedTime()
            })
            .ToList();

        if (toAdd.Count == 0)
            return;

        _dbContext.AmbientThresholdConfigs.AddRange(toAdd);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} AmbientThresholdConfig(s).", toAdd.Count);
    }

    private async Task SeedAmbientReadingsAsync(List<Site> sites, CancellationToken cancellationToken)
    {
        var hasReadings = await _dbContext.AmbientReadings.AnyAsync(cancellationToken);
        if (hasReadings)
            return;

        var now = DateTime.UtcNow;
        var readings = new List<AmbientReading>();

        foreach (var site in sites)
        {
            // 12 reading mỗi 5 phút trong 1h gần nhất — temp tăng dần, humidity ổn định
            for (var i = 11; i >= 0; i--)
            {
                var step = 11 - i;
                readings.Add(new AmbientReading
                {
                    Time = now.AddMinutes(-i * 5),
                    SiteId = site.Id,
                    AmbientTemperature = 30m + step * 0.5m, // 30 → 35.5°C
                    Humidity = 70m + (step % 3),            // 70 ~ 72%RH
                    SolarIrradiance = 600m + step * 20m,    // 600 → 820 W/m²
                    Source = AmbientReadingSourceEnum.WeatherApi,
                    SourceDeviceId = "openmeteo"
                });
            }
        }

        _dbContext.AmbientReadings.AddRange(readings);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} AmbientReading rows across {Sites} site(s).", readings.Count, sites.Count);
    }

    private async Task SeedEnvironmentalIncidentsAsync(List<Site> sites, CancellationToken cancellationToken)
    {
        var hasIncidents = await _dbContext.EnvironmentalIncidents.AnyAsync(cancellationToken);
        if (hasIncidents)
            return;

        var now = DateTime.UtcNow;
        var firstSite = sites[0];
        var incidents = new List<EnvironmentalIncident>
        {
            // Kịch bản 1: incident đang Open — Smoke vừa report.
            new EnvironmentalIncident
            {
                Id = Guid.NewGuid(),
                SiteId = firstSite.Id,
                IncidentType = EnvironmentalIncidentTypeEnum.Smoke,
                Status = EnvironmentalIncidentStatusEnum.Open,
                Severity = AlertSeverityEnum.Critical,
                ReportedBy = "iot-edge-01",
                DetectedAt = now.AddMinutes(-15),
                Notes = "Smoke sensor MQ-2 vượt ngưỡng > 200ppm trong 30s.",
                CreatedAt = now.AddMinutes(-15)
            },
            // Kịch bản 2: incident Acknowledged — Manager đã xác nhận.
            new EnvironmentalIncident
            {
                Id = Guid.NewGuid(),
                SiteId = firstSite.Id,
                IncidentType = EnvironmentalIncidentTypeEnum.OverheatHazard,
                Status = EnvironmentalIncidentStatusEnum.Acknowledged,
                Severity = AlertSeverityEnum.Warning,
                ReportedBy = "iot-edge-02",
                DetectedAt = now.AddHours(-2),
                AcknowledgedBy = Guid.NewGuid(),
                AcknowledgedAt = now.AddHours(-1).AddMinutes(-30),
                Notes = "Ambient temp > 42°C kéo dài 10 phút, đã thông báo kỹ thuật.",
                CreatedAt = now.AddHours(-2)
            },
            // Kịch bản 3: incident đã Resolved — đầy đủ dấu vết audit.
            new EnvironmentalIncident
            {
                Id = Guid.NewGuid(),
                SiteId = firstSite.Id,
                IncidentType = EnvironmentalIncidentTypeEnum.Flood,
                Status = EnvironmentalIncidentStatusEnum.Resolved,
                Severity = AlertSeverityEnum.Warning,
                ReportedBy = "operator-mobile",
                DetectedAt = now.AddDays(-1),
                AcknowledgedBy = Guid.NewGuid(),
                AcknowledgedAt = now.AddDays(-1).AddMinutes(20),
                ResolvedBy = Guid.NewGuid(),
                ResolvedAt = now.AddDays(-1).AddHours(3),
                ResolutionNote = "Mưa lớn — đã bơm thoát nước, kiểm tra cabinet không bị ngập.",
                CreatedAt = now.AddDays(-1)
            }
        };

        // Nếu có thêm site thứ 2 — seed thêm 1 FalseAlarm để cover branch còn lại.
        if (sites.Count > 1)
        {
            var secondSite = sites[1];
            incidents.Add(new EnvironmentalIncident
            {
                Id = Guid.NewGuid(),
                SiteId = secondSite.Id,
                IncidentType = EnvironmentalIncidentTypeEnum.GasLeak,
                Status = EnvironmentalIncidentStatusEnum.FalseAlarm,
                Severity = AlertSeverityEnum.Info,
                ReportedBy = "iot-edge-03",
                DetectedAt = now.AddDays(-2),
                FalseAlarmBy = Guid.NewGuid(),
                FalseAlarmAt = now.AddDays(-2).AddMinutes(45),
                FalseAlarmReason = "Vệ sinh module phun cồn — bay hơi giả gas leak.",
                CreatedAt = now.AddDays(-2)
            });
        }

        _dbContext.EnvironmentalIncidents.AddRange(incidents);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} EnvironmentalIncident(s).", incidents.Count);
    }

    private async Task SeedNoiseBreachEventsAsync(CancellationToken cancellationToken)
    {
        var hasEvents = await _dbContext.NoiseBreachEvents.AnyAsync(cancellationToken);
        if (hasEvents)
            return;

        var assets = await _dbContext.BatteryAssets
            .OrderBy(a => a.SerialNumber)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (assets.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var events = new List<NoiseBreachEvent>();

        // Asset 1 — 3 breach Overheat liên tiếp trong 5 phút (đủ để promote thành Alert)
        for (var i = 0; i < 3; i++)
        {
            events.Add(new NoiseBreachEvent
            {
                Time = now.AddMinutes(-5 + i),
                BatteryAssetId = assets[0].Id,
                AnomalyType = AnomalyTypeEnum.Overheat,
                ThresholdValue = 60m,
                ActualValue = 65m + i,
                Unit = "°C",
                PromotedToAlertId = null,
                SourceType = SensorReadingSourceTypeEnum.IotGateway
            });
        }

        // Asset 2 (nếu có) — 1 breach LowSoc đơn lẻ (không đủ promote — dùng test logic ignore)
        if (assets.Count > 1)
        {
            events.Add(new NoiseBreachEvent
            {
                Time = now.AddMinutes(-3),
                BatteryAssetId = assets[1].Id,
                AnomalyType = AnomalyTypeEnum.LowSoc,
                ThresholdValue = 10m,
                ActualValue = 7.5m,
                Unit = "%",
                PromotedToAlertId = null,
                SourceType = SensorReadingSourceTypeEnum.Bms
            });
        }

        _dbContext.NoiseBreachEvents.AddRange(events);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} NoiseBreachEvent rows.", events.Count);
    }

    private static DateTime SeedTime() => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}
