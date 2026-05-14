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
    private static readonly Guid BlockAGroupId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid BlockBGroupId = Guid.Parse("30000000-0000-0000-0000-000000000002");

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
        await SeedSiteAndGroupsAsync(cancellationToken);
        await SeedAssetsAsync(cancellationToken);
        await SeedThresholdsAsync(cancellationToken);
        await SyncGroupCountsAsync(cancellationToken);
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
            RolesCsv = "Customer",
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
                Description = "Cụm pin NMC công suất lớn cho solar farm.",
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

    private async Task SeedSiteAndGroupsAsync(CancellationToken cancellationToken)
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

        var existingGroupIds = await _dbContext.BatteryGroups
            .Select(group => group.Id)
            .ToListAsync(cancellationToken);

        if (!existingGroupIds.Contains(BlockAGroupId))
        {
            _dbContext.BatteryGroups.Add(new BatteryGroup
            {
                Id = BlockAGroupId,
                SiteId = DefaultSiteId,
                Name = "Block A",
                BatteryTypeId = LiFePo4TypeId,
                BatteryCount = 0,
                CreatedAt = SeedTime()
            });
        }

        if (!existingGroupIds.Contains(BlockBGroupId))
        {
            _dbContext.BatteryGroups.Add(new BatteryGroup
            {
                Id = BlockBGroupId,
                SiteId = DefaultSiteId,
                Name = "Block B",
                BatteryTypeId = NmcTypeId,
                BatteryCount = 0,
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
                BatteryGroupId = BlockAGroupId,
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
                BatteryGroupId = BlockAGroupId,
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
                BatteryGroupId = BlockBGroupId,
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
        _dbContext.ThresholdConfigs.AddRange(
            CreateThreshold(LiFePo4TypeId, 10.5m, 14.6m, -10, 60, 20, 10, now),
            CreateThreshold(NmcTypeId, 42m, 54.6m, -10, 55, 25, 15, now),
            CreateThreshold(NcaTypeId, 21m, 29.2m, -10, 55, 25, 15, now));

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SyncGroupCountsAsync(CancellationToken cancellationToken)
    {
        var groups = await _dbContext.BatteryGroups.ToListAsync(cancellationToken);

        foreach (var group in groups)
        {
            group.BatteryCount = await _dbContext.BatteryAssets
                .CountAsync(asset => asset.BatteryGroupId == group.Id && !asset.IsDeleted, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Battery seed data checked.");
    }

    private static ThresholdConfig CreateThreshold(
        Guid batteryTypeId,
        decimal voltageMin,
        decimal voltageMax,
        decimal temperatureMin,
        decimal temperatureMax,
        decimal socWarning,
        decimal socCritical,
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
