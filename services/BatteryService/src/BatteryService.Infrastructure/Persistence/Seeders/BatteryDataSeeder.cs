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
        await SeedSiteAsync(customerId, cancellationToken);
        await SeedThresholdsAsync(liFePo4TypeId, nmcTypeId, ncaTypeId, cancellationToken);
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
        // `voltageMin`/`temperatureMin` là mốc WARNING, `voltageMax`/`temperatureMax` là mốc
        // CRITICAL — thang một chiều, không phải hai đầu dải an toàn (xem `AnomalyRules.Detect`).
        //
        // Số cũ (vd LiFePO4 10.5–14.6V, −10..60°C) là dải an toàn theo nghĩa CŨ; giữ nguyên thì
        // dưới nghĩa mới thành "cảnh báo khi trên 10.5V" — tức mọi số đo đều Warning. Đặt lại theo
        // giới hạn sạc đầy của từng hoá học, Critical cao hơn Warning một biên nhỏ.
        _dbContext.ThresholdConfigs.AddRange(
            // 4S LiFePO4 12.8V — đầy 14.6V (3.65V/cell).
            CreateThreshold(liFePo4TypeId, 14.6m, 15.2m, 55, 60, 20, 10, sohWarning: 85m, sohCritical: 75m, now),
            // 13S NMC 48V — đầy 54.6V (4.2V/cell).
            CreateThreshold(nmcTypeId, 54.6m, 56.0m, 50, 55, 25, 15, sohWarning: 85m, sohCritical: 75m, now),
            // 8S LiFePO4 24V (nhãn NCA) — đầy 29.2V.
            CreateThreshold(ncaTypeId, 29.2m, 30.0m, 50, 55, 25, 15, sohWarning: 85m, sohCritical: 75m, now));

        await _dbContext.SaveChangesAsync(cancellationToken);
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
