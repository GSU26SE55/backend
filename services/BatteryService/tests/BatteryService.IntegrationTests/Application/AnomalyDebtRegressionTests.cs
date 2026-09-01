using BatteryService.Application.Anomaly;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Implements.Repositories;
using BatteryService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace BatteryService.IntegrationTests.Application;

/// <summary>
/// IOT3-106 (#1172) — chặn hồi quy cho hai nợ kỹ thuật ở <c>AnomalyDetectionService</c>, phát hiện
/// khi chạy end-to-end thật ngày 2026-08-08.
/// </summary>
/// <remarks>
/// <para>
/// <b>M3</b> — <c>PromotedToAlertId</c> không bao giờ được gán (đo được: 0/11 bản ghi toàn bảng).<br/>
/// <b>M4</b> — dedup không thấy alert do CHÍNH lượt quét đó vừa tạo (đo được: 6 reading vi phạm
/// cách nhau 2 giây → 5 alert <c>Open</c> trùng nhau cho một sự cố).
/// </para>
/// <para>
/// <b>⚠️ VÌ SAO Ở ĐÂY CHỨ KHÔNG PHẢI `BatteryService.UnitTests`.</b> Đã thử viết ở đó trước và
/// <b>thất bại</b>: `MockUnitOfWorkBuilder` cài `AddAsync` là <c>list.Add(e)</c> ngay lập tức, mà
/// <c>GetAllAsync()</c> trả về chính list đó. Nghĩa là với mock, thực thể vừa Add <b>hiện ra ngay</b>
/// với mọi truy vấn — <b>ngược hẳn</b> EF Core, nơi thực thể chưa <c>SaveChanges</c> KHÔNG nằm trong
/// kết quả truy vấn cơ sở dữ liệu.
/// </para>
/// <para>
/// Kiểm chứng: tạm lùi cả hai bản sửa rồi chạy lại — bộ test viết trên mock vẫn <b>xanh cả 4 bài</b>.
/// Đó chính là lý do hai lỗi này sống sót qua 657 unit test. Bản khác biệt giữa change tracker và
/// DB là <b>bản chất</b> của cả hai lỗi, nên bài test bắt buộc phải chạy trên <c>ApplicationDbContext</c>
/// thật.
/// </para>
/// </remarks>
public class AnomalyDebtRegressionTests
{
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid BatteryTypeId = Guid.NewGuid();
    private static readonly Guid AssetId = Guid.NewGuid();
    private static readonly Guid AssetId2 = Guid.NewGuid();

    private static ApplicationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"iot3-106-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            new AuditableEntityInterceptor(new CurrentUserService(new HttpContextAccessor())));

    private static AnomalyDetectionService Sut(ApplicationDbContext db) =>
        new(new UnitOfWork(db), Options.Create(new AnomalyEngineOptions { DedupWindowMinutes = 30 }));

    private static async Task SeedAsync(ApplicationDbContext db, bool noiseSuppression, int noiseCount = 3)
    {
        db.CustomerAccounts.Add(new CustomerAccount
        {
            Id = CustomerId,
            Email = "iot3-106@x.com",
            FullName = "Debt Regression",
            Role = "Customer",
            IsActive = true,
            LastSyncedAtUtc = DateTime.UtcNow
        });
        db.BatteryTypes.Add(new BatteryType
        {
            Id = BatteryTypeId,
            Name = "LiFePO4 Debt Test",
            NominalCapacityAh = 100,
            NominalVoltage = 12,
            Chemistry = BatteryChemistryEnum.LiFePO4,
            MaxCycleCount = 2000
        });
        foreach (var (id, serial) in new[] { (AssetId, "BAT-DEBT-1"), (AssetId2, "BAT-DEBT-2") })
        {
            db.BatteryAssets.Add(new BatteryAsset
            {
                Id = id,
                SerialNumber = serial,
                BatteryTypeId = BatteryTypeId,
                CustomerId = CustomerId,
                InstallDate = DateTime.UtcNow.AddDays(-30),
                Status = BatteryStatusEnum.Active,
                WarrantyStatus = WarrantyStatusEnum.Active
            });
        }
        db.ThresholdConfigs.Add(new ThresholdConfig
        {
            Id = Guid.NewGuid(),
            BatteryTypeId = BatteryTypeId,
            VoltageMin = 14,
            VoltageMax = 15,
            TemperatureMin = 45,
            TemperatureMax = 50,
            SocWarningThreshold = 20,
            SocCriticalThreshold = 10,
            NoiseSuppressionEnabled = noiseSuppression,
            NoiseSuppressionCount = noiseCount,
            NoiseSuppressionWindowHours = 24,
            EffectiveFromUtc = DateTime.UtcNow,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Quá áp (20 V &gt; VoltageMax 14) — mỗi reading một mốc riêng, như thiết bị thật gửi.</summary>
    private static void AddOvervoltage(ApplicationDbContext db, Guid assetId, int secondsAgo) =>
        db.SensorReadings.Add(new SensorReading
        {
            Time = DateTime.UtcNow.AddSeconds(-secondsAgo),
            BatteryAssetId = assetId,
            Voltage = 20m,
            Current = 1m,
            Temperature = 25m,
            SocPercent = 50m,
            SensorSourceCode = "primary"
        });

    // ================================================================== M4

    /// <summary>
    /// IOT3-106/M4 — nhiều reading vi phạm trong CÙNG một lượt quét phải cho ra ĐÚNG MỘT alert
    /// <c>Open</c>; phần còn lại là <c>Merged</c> trỏ về nó.
    /// </summary>
    /// <remarks>
    /// Trước bản sửa, <c>FindActiveAlertToMergeAsync</c> hỏi DB bằng <c>.FirstOrDefaultAsync()</c>
    /// nên không thấy alert vừa <c>AddAsync</c> còn pending ⇒ mỗi reading tự tạo một alert
    /// <c>Open</c> mới. Người trực nhận N cảnh báo cho MỘT sự cố.
    /// <para>
    /// ⚠️ Bảng alert PHẢI bắt đầu từ RỖNG. Có sẵn một alert cùng loại còn trong
    /// <c>DedupWindowEndUtc</c> thì reading đầu tìm thấy cha ngay và mọi alert sau đều
    /// <c>Merged</c> — bài test sẽ ĐẠT trong khi lỗi còn nguyên. Đó đúng là cách lỗi này ẩn mình.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Scan_ManyBreachesInOnePass_CreatesExactlyOneOpenAlert()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db, noiseSuppression: false);

        foreach (var s in new[] { 50, 40, 30, 20, 10, 5 })
            AddOvervoltage(db, AssetId, s);
        await db.SaveChangesAsync();

        db.Alerts.Should().BeEmpty("điều kiện tiên quyết: bảng alert phải rỗng, nếu không lỗi bị che");

        await Sut(db).ScanRecentReadingsAsync(TimeSpan.FromMinutes(5));

        var alerts = await db.Alerts
            .Where(a => a.AnomalyType == AnomalyTypeEnum.Overvoltage)
            .ToListAsync();

        alerts.Should().HaveCount(6, "mỗi reading vi phạm để lại một bản ghi");

        var open = alerts.Where(a => a.Status == AlertStatusEnum.Open).ToList();
        open.Should().HaveCount(1,
            "sáu reading của CÙNG một sự cố chỉ được sinh MỘT cảnh báo cho người trực; nhiều hơn "
            + "nghĩa là dedup lại mù với alert do chính lượt quét này tạo (nợ #4)");

        var merged = alerts.Where(a => a.Status == AlertStatusEnum.Merged).ToList();
        merged.Should().HaveCount(5);
        merged.Should().OnlyContain(a => a.MergedIntoAlertId == open[0].Id,
            "mọi bản gộp phải trỏ về đúng alert gốc, không phải về nhau");
    }

    /// <summary>Hai pin khác nhau vi phạm cùng lượt quét ⇒ mỗi pin một alert riêng.</summary>
    /// <remarks>
    /// Chốt rằng bản sửa gom theo <c>(assetId, anomalyType)</c> chứ không gom bừa — gom quá tay sẽ
    /// giấu mất sự cố của pin thứ hai, đổi một lỗi ồn ào lấy một lỗi im lặng.
    /// </remarks>
    [Fact]
    public async Task Scan_TwoAssetsInOnePass_KeepsAlertsSeparate()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db, noiseSuppression: false);

        AddOvervoltage(db, AssetId, 40);
        AddOvervoltage(db, AssetId2, 35);
        AddOvervoltage(db, AssetId, 30);
        AddOvervoltage(db, AssetId2, 25);
        await db.SaveChangesAsync();

        await Sut(db).ScanRecentReadingsAsync(TimeSpan.FromMinutes(5));

        var open = await db.Alerts
            .Where(a => a.Status == AlertStatusEnum.Open
                        && a.AnomalyType == AnomalyTypeEnum.Overvoltage)
            .ToListAsync();

        open.Should().HaveCount(2, "hai pin khác nhau là hai sự cố khác nhau");
        open.Select(a => a.BatteryAssetId).Should().BeEquivalentTo(new[] { AssetId, AssetId2 });
    }

    /// <summary>Alert cũ còn trong cửa sổ dedup ⇒ alert mới gộp vào nó, không tạo cái thứ hai.</summary>
    /// <remarks>
    /// Chốt rằng bản sửa KHÔNG phá đường dedup vốn có (tra DB) khi tra từ điển cục bộ không thấy gì.
    /// </remarks>
    [Fact]
    public async Task Scan_WhenOpenAlertAlreadyPersisted_MergesIntoIt()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db, noiseSuppression: false);

        var parentId = Guid.NewGuid();
        db.Alerts.Add(new Alert
        {
            Id = parentId,
            BatteryAssetId = AssetId,
            AnomalyType = AnomalyTypeEnum.Overvoltage,
            Severity = AlertSeverityEnum.Critical,
            ThresholdValue = 14m,
            ActualValue = 20m,
            Unit = "V",
            DetectedAt = DateTime.UtcNow.AddMinutes(-1),
            Status = AlertStatusEnum.Open,
            DedupWindowEndUtc = DateTime.UtcNow.AddMinutes(29)
        });
        AddOvervoltage(db, AssetId, 10);
        await db.SaveChangesAsync();

        await Sut(db).ScanRecentReadingsAsync(TimeSpan.FromMinutes(5));

        var open = await db.Alerts.Where(a => a.Status == AlertStatusEnum.Open).ToListAsync();
        open.Should().HaveCount(1);
        open[0].Id.Should().Be(parentId, "phải gộp vào alert cũ, không tạo alert Open thứ hai");

        var merged = await db.Alerts.Where(a => a.Status == AlertStatusEnum.Merged).ToListAsync();
        merged.Should().ContainSingle().Which.MergedIntoAlertId.Should().Be(parentId);
    }

    // ================================================================== M3

    /// <summary>
    /// IOT3-106/M3 — alert nổ qua đường chống nhiễu ⇒ cả chuỗi breach phải được gán
    /// <c>PromotedToAlertId</c>.
    /// </summary>
    /// <remarks>
    /// Trước bản sửa, lời gọi bị gác bằng <c>if (recordedBreach is not null)</c>, mà alert của đường
    /// chống nhiễu CHỈ nổ ở lượt quét LẠI — lượt đó <c>recordedBreach</c> luôn null. Hai điều kiện
    /// loại trừ nhau nên đường promote không bao giờ chạy.
    /// <para>
    /// Hậu quả nặng hơn phần audit: XML doc ghi <i>"retention sẽ giữ các row đã promote"</i>. Không
    /// row nào được đánh dấu nghĩa là retention sẽ xoá sạch chuỗi breach làm bằng chứng cho alert.
    /// </para>
    /// <para>
    /// Bài này chạy <b>HAI</b> lượt quét — điều kiện bắt buộc để tái hiện: lượt đầu ghi breach và
    /// chặn alert, lượt sau mới cho alert nổ với <c>recordedBreach == null</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Scan_AlertFiringOnSecondPass_PromotesTheWholeBreachChain()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db, noiseSuppression: true, noiseCount: 3);

        foreach (var s in new[] { 40, 30, 20 })
            AddOvervoltage(db, AssetId, s);
        await db.SaveChangesAsync();

        var sut = Sut(db);

        // Lượt 1 — ghi breach, alert bị chặn (chưa đủ ngưỡng 3).
        var pass1 = await sut.ScanRecentReadingsAsync(TimeSpan.FromMinutes(5));
        pass1.AlertsSuppressed.Should().BeGreaterThan(0, "ngưỡng 3 phải chặn được reading đầu");

        // Lượt 2 — breach đã persisted, `alreadyRecorded == true` ⇒ `recordedBreach == null`.
        // Đây chính là lượt mà bản cũ bỏ qua việc promote.
        await sut.ScanRecentReadingsAsync(TimeSpan.FromMinutes(5));

        var alert = await db.Alerts.FirstOrDefaultAsync(
            a => a.Status == AlertStatusEnum.Open && a.AnomalyType == AnomalyTypeEnum.Overvoltage);
        alert.Should().NotBeNull("đủ 3 lần vi phạm thì alert phải nổ ở lượt quét thứ hai");

        var breaches = await db.NoiseBreachEvents
            .Where(n => n.AnomalyType == AnomalyTypeEnum.Overvoltage)
            .ToListAsync();
        breaches.Should().NotBeEmpty();

        breaches.Should().Contain(n => n.PromotedToAlertId == alert!.Id,
            "chuỗi breach phải được gán về alert vừa nổ — nếu không, retention sẽ xoá mất bằng "
            + "chứng của chính alert đó (nợ #3)");
    }

    /// <summary>Chống nhiễu TẮT ⇒ không ghi breach nào, nên cũng không có gì để promote.</summary>
    /// <remarks>
    /// Chốt rằng bản sửa gác theo <c>threshold</c> chứ không bỏ gác hoàn toàn — bỏ hẳn sẽ gọi
    /// <c>PromoteBreachChainAsync</c> cho cả alert không đi qua đường chống nhiễu, tốn một truy vấn
    /// vô ích mỗi alert.
    /// </remarks>
    [Fact]
    public async Task Scan_WhenNoiseSuppressionDisabled_WritesNoBreachAtAll()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db, noiseSuppression: false);

        AddOvervoltage(db, AssetId, 20);
        AddOvervoltage(db, AssetId, 10);
        await db.SaveChangesAsync();

        await Sut(db).ScanRecentReadingsAsync(TimeSpan.FromMinutes(5));

        (await db.NoiseBreachEvents.ToListAsync()).Should().BeEmpty(
            "chống nhiễu tắt thì không ghi breach, nên cũng không có gì để promote");
    }
}
