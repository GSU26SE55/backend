using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BatteryService.Infrastructure.Persistence.Seeders;

public class BatteryDataSeeder
{
    private static readonly Guid SampleCustomerId = Guid.Parse("44444444-4444-4444-4444-000000000001");
    private static readonly Guid LiFePo4TypeId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid NmcTypeId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid NcaTypeId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid DefaultSiteId = Guid.Parse("20000000-0000-0000-0000-000000000001");

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<BatteryDataSeeder> _logger;

    public BatteryDataSeeder(ApplicationDbContext dbContext, ILogger<BatteryDataSeeder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedCustomerAccountsAsync(cancellationToken);
        await SeedBatteryTypesAsync(cancellationToken);
        await SeedSiteAsync(cancellationToken);
        await SeedAssetsAsync(cancellationToken);
        await SeedThresholdsAsync(cancellationToken);
        await SeedAnomalyScenarioReadingsAsync(cancellationToken);
    }

    private async Task SeedCustomerAccountsAsync(CancellationToken cancellationToken)
    {
        var exists = await _dbContext.CustomerAccounts.AnyAsync(account => account.Id == SampleCustomerId, cancellationToken);
        if (exists)
            return;

        _dbContext.CustomerAccounts.Add(new CustomerAccount
        {
            Id = SampleCustomerId,
            Email = "sample.customer@solarbattery.local",
            FullName = "Sample Battery Customer",
            PhoneNumber = "0900000001",
            Role = "Customer",
            IsActive = true,
            LastSyncedAtUtc = SeedTime(),
            CreatedAt = SeedTime()
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedBatteryTypesAsync(CancellationToken cancellationToken)
    {
        var existingIds = await _dbContext.BatteryTypes
            .Select(type => type.Id)
            .ToListAsync(cancellationToken);

        var seedTypes = new[]
        {
            new BatteryType
            {
                Id = LiFePo4TypeId,
                Name = "LiFePO4 12V 100Ah",
                Manufacturer = "SolarCo",
                NominalCapacityAh = 100,
                NominalVoltage = 12,
                Chemistry = BatteryChemistryEnum.LiFePO4,
                MaxCycleCount = 3000,
                Description = "Pin lithium iron phosphate dùng cho hệ solar dân dụng.",
                CreatedAt = SeedTime()
            },
            new BatteryType
            {
                Id = NmcTypeId,
                Name = "NMC 48V 200Ah",
                Manufacturer = "SunGrid",
                NominalCapacityAh = 200,
                NominalVoltage = 48,
                Chemistry = BatteryChemistryEnum.Nmc,
                MaxCycleCount = 2500,
                Description = "Pin NMC công suất lớn cho solar farm.",
                CreatedAt = SeedTime()
            },
            new BatteryType
            {
                Id = NcaTypeId,
                Name = "NCA 24V 150Ah",
                Manufacturer = "VoltMax",
                NominalCapacityAh = 150,
                NominalVoltage = 24,
                Chemistry = BatteryChemistryEnum.Nca,
                MaxCycleCount = 2200,
                Description = "Pin NCA dùng cho cụm lưu trữ trung bình.",
                CreatedAt = SeedTime()
            }
        };

        foreach (var seed in seedTypes.Where(seed => !existingIds.Contains(seed.Id)))
            _dbContext.BatteryTypes.Add(seed);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedSiteAsync(CancellationToken cancellationToken)
    {
        var siteExists = await _dbContext.Sites.AnyAsync(site => site.Id == DefaultSiteId, cancellationToken);
        if (!siteExists)
        {
            _dbContext.Sites.Add(new Site
            {
                Id = DefaultSiteId,
                Name = "Solar Farm Long An",
                CustomerId = SampleCustomerId,
                Address = "Long An, Vietnam",
                Latitude = 10.695m,
                Longitude = 106.243m,
                CapacityKw = 500,
                InstallDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                Status = SiteStatusEnum.Active,
                ContactPersonName = "Nguyen Van A",
                ContactPersonPhone = "0900000001",
                CreatedAt = SeedTime()
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedAssetsAsync(CancellationToken cancellationToken)
    {
        var existingSerials = await _dbContext.BatteryAssets
            .Select(asset => asset.SerialNumber)
            .ToListAsync(cancellationToken);

        var seedAssets = new[]
        {
            new BatteryAsset
            {
                Id = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                SerialNumber = "BAT-2026-001",
                BatteryTypeId = LiFePo4TypeId,
                SiteId = DefaultSiteId,
                CustomerId = SampleCustomerId,
                InstallDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                WarrantyEndDate = new DateTime(2031, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                WarrantyStatus = WarrantyStatusEnum.Active,
                Location = "Block A - Rack 01",
                Latitude = 10.695m,
                Longitude = 106.243m,
                Status = BatteryStatusEnum.Active,
                CreatedAt = SeedTime()
            },
            new BatteryAsset
            {
                Id = Guid.Parse("40000000-0000-0000-0000-000000000002"),
                SerialNumber = "BAT-2026-002",
                BatteryTypeId = LiFePo4TypeId,
                SiteId = DefaultSiteId,
                CustomerId = SampleCustomerId,
                InstallDate = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc),
                WarrantyEndDate = new DateTime(2031, 1, 20, 0, 0, 0, DateTimeKind.Utc),
                WarrantyStatus = WarrantyStatusEnum.Active,
                Location = "Block A - Rack 02",
                Latitude = 10.696m,
                Longitude = 106.244m,
                Status = BatteryStatusEnum.Active,
                CreatedAt = SeedTime()
            },
            new BatteryAsset
            {
                Id = Guid.Parse("40000000-0000-0000-0000-000000000003"),
                SerialNumber = "BAT-2026-003",
                BatteryTypeId = NmcTypeId,
                SiteId = DefaultSiteId,
                CustomerId = SampleCustomerId,
                InstallDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                WarrantyEndDate = new DateTime(2031, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                WarrantyStatus = WarrantyStatusEnum.Active,
                Location = "Block B - Rack 01",
                Latitude = 10.697m,
                Longitude = 106.245m,
                Status = BatteryStatusEnum.Active,
                CreatedAt = SeedTime()
            }
        };

        foreach (var seed in seedAssets.Where(seed => !existingSerials.Contains(seed.SerialNumber)))
            _dbContext.BatteryAssets.Add(seed);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedThresholdsAsync(CancellationToken cancellationToken)
    {
        var hasThresholds = await _dbContext.ThresholdConfigs.AnyAsync(cancellationToken);
        if (hasThresholds)
            return;

        var now = SeedTime();
        // 3 BatteryType — kèm SOH threshold (Tier 1 Sprint 3): EOL khi SOH ≤ 75% (Critical), warning từ 85%
        _dbContext.ThresholdConfigs.AddRange(
            CreateThreshold(LiFePo4TypeId, 10.5m, 14.6m, -10, 60, 20, 10, sohWarning: 85m, sohCritical: 75m, now),
            CreateThreshold(NmcTypeId, 42m, 54.6m, -10, 55, 25, 15, sohWarning: 85m, sohCritical: 75m, now),
            CreateThreshold(NcaTypeId, 21m, 29.2m, -10, 55, 25, 15, sohWarning: 85m, sohCritical: 75m, now));

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Seed sensor readings có sẵn các kịch bản anomaly để demo + integration test:
    /// - Asset 1: pin bình thường (V/T/SOC trong ngưỡng, SOH 95%)
    /// - Asset 2: pin Overheat (Temperature vượt > 5°C ngưỡng → Critical)
    /// - Asset 3: pin LowSoc (SOC dưới SocCritical → Critical)
    /// - Asset 4: pin SohDegradation (SOH 72% &lt; SohCritical 75% → Critical)
    /// </summary>
    private async Task SeedAnomalyScenarioReadingsAsync(CancellationToken cancellationToken)
    {
        var hasReadings = await _dbContext.SensorReadings.AnyAsync(cancellationToken);
        if (hasReadings)
            return;

        var assets = await _dbContext.BatteryAssets
            .OrderBy(a => a.SerialNumber)
            .Take(4)
            .ToListAsync(cancellationToken);
        if (assets.Count < 4)
            return; // không đủ asset để seed scenarios

        var now = DateTime.UtcNow;
        var readings = new List<SensorReading>();

        // Asset 1 — bình thường (12 reading mỗi 5 phút trong 1 giờ qua)
        for (var i = 11; i >= 0; i--)
        {
            readings.Add(new SensorReading
            {
                Time = now.AddMinutes(-i * 5),
                BatteryAssetId = assets[0].Id,
                Voltage = 12.8m,
                Current = 2.5m,
                Temperature = 28m,
                SocPercent = 75m,
                CycleCount = 120,
                SohPercent = 95m,
                ChargingState = ChargingStateEnum.Charging,
                SourceDeviceId = "seed-normal"
            });
        }

        // Asset 2 — Overheat trending (Temperature tăng dần lên 70°C, vượt TemperatureMax 60°C của LiFePO4 > 5°C → Critical)
        for (var i = 11; i >= 0; i--)
        {
            var temp = 50m + (11 - i) * 2m; // 50 → 72°C
            readings.Add(new SensorReading
            {
                Time = now.AddMinutes(-i * 5),
                BatteryAssetId = assets[1].Id,
                Voltage = 13m,
                Current = 5m,
                Temperature = temp,
                SocPercent = 60m,
                CycleCount = 250,
                SohPercent = 90m,
                ChargingState = ChargingStateEnum.Discharging,
                SourceDeviceId = "seed-overheat"
            });
        }

        // Asset 3 — LowSoc (SOC tụt từ 30 → 5% — dưới SocCritical 10 → Critical)
        for (var i = 11; i >= 0; i--)
        {
            var soc = 30m - (11 - i) * 2.3m; // 30 → 4.7%
            if (soc < 0)
                soc = 0;
            readings.Add(new SensorReading
            {
                Time = now.AddMinutes(-i * 5),
                BatteryAssetId = assets[2].Id,
                Voltage = 11m,
                Current = -3m,
                Temperature = 30m,
                SocPercent = soc,
                CycleCount = 400,
                SohPercent = 88m,
                ChargingState = ChargingStateEnum.Discharging,
                SourceDeviceId = "seed-lowsoc"
            });
        }

        // Asset 4 — SohDegradation (SOH 72% — pin xuống cấp gần EOL, dưới SohCritical 75 → Critical)
        for (var i = 11; i >= 0; i--)
        {
            readings.Add(new SensorReading
            {
                Time = now.AddMinutes(-i * 5),
                BatteryAssetId = assets[3].Id,
                Voltage = 12.5m,
                Current = 1m,
                Temperature = 32m,
                SocPercent = 55m,
                CycleCount = 1800,
                SohPercent = 72m,
                ChargingState = ChargingStateEnum.Idle,
                SourceDeviceId = "seed-soh-degradation"
            });
        }

        _dbContext.SensorReadings.AddRange(readings);

        // Cập nhật LastSensorReadingAt cho từng asset (= reading mới nhất)
        foreach (var asset in assets)
            asset.LastSensorReadingAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} sensor readings across {Assets} anomaly scenarios", readings.Count, assets.Count);
    }

    private static ThresholdConfig CreateThreshold(
        Guid batteryTypeId,
        decimal voltageMin,
        decimal voltageMax,
        decimal temperatureMin,
        decimal temperatureMax,
        decimal socWarning,
        decimal socCritical,
        decimal sohWarning,
        decimal sohCritical,
        DateTime now)
    {
        return new ThresholdConfig
        {
            Id = Guid.NewGuid(),
            BatteryTypeId = batteryTypeId,
            VoltageMin = voltageMin,
            VoltageMax = voltageMax,
            TemperatureMin = temperatureMin,
            TemperatureMax = temperatureMax,
            SocWarningThreshold = socWarning,
            SocCriticalThreshold = socCritical,
            SohWarningThreshold = sohWarning,
            SohCriticalThreshold = sohCritical,
            EffectiveFromUtc = now,
            IsActive = true,
            CreatedAt = now
        };
    }

    private static DateTime SeedTime()
    {
        return new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
