using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BatteryService.Infrastructure.Persistence.Seeders;

public class BatteryDataSeeder
{
    private const string SampleCustomerEmail = "sample.customer@solarbattery.local";
    private const string LiFePo4TypeName = "LiFePO4 12V 100Ah";
    private const string NmcTypeName = "NMC 48V 200Ah";
    private const string NcaTypeName = "NCA 24V 150Ah";
    private const string DefaultSiteName = "Solar Farm Long An";

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<BatteryDataSeeder> _logger;

    public BatteryDataSeeder(ApplicationDbContext dbContext, ILogger<BatteryDataSeeder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var customerId = await SeedCustomerAccountsAsync(cancellationToken);
        var (liFePo4TypeId, nmcTypeId, ncaTypeId) = await SeedBatteryTypesAsync(cancellationToken);
        var siteId = await SeedSiteAsync(customerId, cancellationToken);
        await SeedAssetsAsync(siteId, customerId, liFePo4TypeId, nmcTypeId, cancellationToken);
        await SeedThresholdsAsync(liFePo4TypeId, nmcTypeId, ncaTypeId, cancellationToken);
        await SeedAnomalyScenarioReadingsAsync(cancellationToken);
    }

    private async Task<Guid> SeedCustomerAccountsAsync(CancellationToken cancellationToken)
    {
        var existing = await _dbContext.CustomerAccounts
            .FirstOrDefaultAsync(a => a.Email == SampleCustomerEmail, cancellationToken);
        if (existing != null)
            return existing.Id;

        var entity = new CustomerAccount
        {
            Id = Guid.NewGuid(),
            Email = SampleCustomerEmail,
            FullName = "Sample Battery Customer",
            PhoneNumber = "0900000001",
            Role = "Customer",
            IsActive = true,
            LastSyncedAtUtc = SeedTime(),
            CreatedAt = SeedTime()
        };
        _dbContext.CustomerAccounts.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    private async Task<(Guid LiFePo4, Guid Nmc, Guid Nca)> SeedBatteryTypesAsync(CancellationToken cancellationToken)
    {
        var existing = await _dbContext.BatteryTypes
            .Where(t => t.Name == LiFePo4TypeName || t.Name == NmcTypeName || t.Name == NcaTypeName)
            .ToDictionaryAsync(t => t.Name, t => t.Id, cancellationToken);

        var seedTypes = new List<BatteryType>();

        if (!existing.ContainsKey(LiFePo4TypeName))
            seedTypes.Add(new BatteryType
            {
                Id = Guid.NewGuid(),
                Name = LiFePo4TypeName,
                Manufacturer = "SolarCo",
                NominalCapacityAh = 100,
                NominalVoltage = 12,
                Chemistry = BatteryChemistryEnum.LiFePO4,
                MaxCycleCount = 3000,
                Description = "Lithium iron phosphate battery for residential solar systems.",
                CreatedAt = SeedTime()
            });

        if (!existing.ContainsKey(NmcTypeName))
            seedTypes.Add(new BatteryType
            {
                Id = Guid.NewGuid(),
                Name = NmcTypeName,
                Manufacturer = "SunGrid",
                NominalCapacityAh = 200,
                NominalVoltage = 48,
                Chemistry = BatteryChemistryEnum.Nmc,
                MaxCycleCount = 2500,
                Description = "High-capacity NMC battery for solar farms.",
                CreatedAt = SeedTime()
            });

        if (!existing.ContainsKey(NcaTypeName))
            seedTypes.Add(new BatteryType
            {
                Id = Guid.NewGuid(),
                Name = NcaTypeName,
                Manufacturer = "VoltMax",
                NominalCapacityAh = 150,
                NominalVoltage = 24,
                Chemistry = BatteryChemistryEnum.Nca,
                MaxCycleCount = 2200,
                Description = "NCA battery for medium-scale energy storage clusters.",
                CreatedAt = SeedTime()
            });

        if (seedTypes.Count > 0)
        {
            _dbContext.BatteryTypes.AddRange(seedTypes);
            await _dbContext.SaveChangesAsync(cancellationToken);
            foreach (var t in seedTypes)
                existing[t.Name] = t.Id;
        }

        return (existing[LiFePo4TypeName], existing[NmcTypeName], existing[NcaTypeName]);
    }

    private async Task<Guid> SeedSiteAsync(Guid customerId, CancellationToken cancellationToken)
    {
        // Khớp theo tên thôi: site demo có thể đã được gán sang customer thật (sau khi đồng bộ
        // với AuthService), thêm CustomerId vào điều kiện là seed lại một site trùng tên mỗi lần
        // service khởi động.
        var existing = await _dbContext.Sites
            .FirstOrDefaultAsync(s => s.Name == DefaultSiteName, cancellationToken);
        if (existing != null)
            return existing.Id;

        var entity = new Site
        {
            Id = Guid.NewGuid(),
            Name = DefaultSiteName,
            CustomerId = customerId,
            Address = "Long An, Vietnam",
            Latitude = 10.695m,
            Longitude = 106.243m,
            InstallDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            Status = SiteStatusEnum.Active,
            ContactPersonName = "Nguyen Van A",
            ContactPersonPhone = "0900000001",
            CreatedAt = SeedTime()
        };
        _dbContext.Sites.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    private async Task SeedAssetsAsync(
        Guid siteId,
        Guid customerId,
        Guid liFePo4TypeId,
        Guid nmcTypeId,
        CancellationToken cancellationToken)
    {
        var existingSerials = await _dbContext.BatteryAssets
            .Select(asset => asset.SerialNumber)
            .ToListAsync(cancellationToken);

        var seedAssets = new[]
        {
            new BatteryAsset
            {
                Id = Guid.NewGuid(),
                SerialNumber = "BAT-2026-001",
                BatteryTypeId = liFePo4TypeId,
                SiteId = siteId,
                CustomerId = customerId,
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
                Id = Guid.NewGuid(),
                SerialNumber = "BAT-2026-002",
                BatteryTypeId = liFePo4TypeId,
                SiteId = siteId,
                CustomerId = customerId,
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
                Id = Guid.NewGuid(),
                SerialNumber = "BAT-2026-003",
                BatteryTypeId = nmcTypeId,
                SiteId = siteId,
                CustomerId = customerId,
                InstallDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                WarrantyEndDate = new DateTime(2031, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                WarrantyStatus = WarrantyStatusEnum.Active,
                Location = "Block B - Rack 01",
                Latitude = 10.697m,
                Longitude = 106.245m,
                Status = BatteryStatusEnum.Active,
                CreatedAt = SeedTime()
            },
            new BatteryAsset
            {
                Id = Guid.NewGuid(),
                SerialNumber = "BAT-2026-004",
                BatteryTypeId = nmcTypeId,
                SiteId = siteId,
                CustomerId = customerId,
                InstallDate = new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc),
                WarrantyEndDate = new DateTime(2031, 2, 5, 0, 0, 0, DateTimeKind.Utc),
                WarrantyStatus = WarrantyStatusEnum.Active,
                Location = "Block B - Rack 02",
                Latitude = 10.698m,
                Longitude = 106.246m,
                Status = BatteryStatusEnum.Active,
                CreatedAt = SeedTime()
            }
        };

        foreach (var seed in seedAssets.Where(seed => !existingSerials.Contains(seed.SerialNumber)))
            _dbContext.BatteryAssets.Add(seed);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedThresholdsAsync(
        Guid liFePo4TypeId,
        Guid nmcTypeId,
        Guid ncaTypeId,
        CancellationToken cancellationToken)
    {
        var hasThresholds = await _dbContext.ThresholdConfigs.AnyAsync(cancellationToken);
        if (hasThresholds)
            return;

        var now = SeedTime();
        // 3 BatteryType — kèm SOH threshold (Tier 1 Sprint 3): EOL khi SOH ≤ 75% (Critical), warning từ 85%
        _dbContext.ThresholdConfigs.AddRange(
            CreateThreshold(liFePo4TypeId, 10.5m, 14.6m, -10, 60, 20, 10, sohWarning: 85m, sohCritical: 75m, now),
            CreateThreshold(nmcTypeId, 42m, 54.6m, -10, 55, 25, 15, sohWarning: 85m, sohCritical: 75m, now),
            CreateThreshold(ncaTypeId, 21m, 29.2m, -10, 55, 25, 15, sohWarning: 85m, sohCritical: 75m, now));

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
