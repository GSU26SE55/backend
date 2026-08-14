using BatteryService.Application.CQRS.Command.SensorReading;
using BatteryService.Application.CQRS.Handler.SensorReading;
using BatteryService.Domain.Entities;
using BatteryService.Infrastructure.Implements.Repositories;
using BatteryService.Infrastructure.Persistence;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace BatteryService.IntegrationTests.Application;

/// <summary>
/// GH-763 — gửi lại telemetry đã có trả 500 thay vì bỏ qua một cách bình thản.
///
/// <para>
/// Lỗi gốc: <c>sensor_readings</c> có PK tổ hợp <c>(time, battery_asset_id)</c>. Handler add mọi
/// item hợp lệ rồi mới <c>SaveChangesAsync</c>, không hề dò trùng. Chỉ cần một số đo đã tồn tại
/// là Postgres ném <c>23505</c> ⇒ API trả 500 ⇒ CẢ batch rollback, kể cả các số đo MỚI. Thiết bị
/// gửi lại thì lại 500 y hệt, nên dữ liệu mới KHÔNG BAO GIỜ vào được — đúng như bằng chứng
/// runtime trong issue (correlation 29f9d19376014b05b9c55c6ce87e2ac3).
/// </para>
/// <para>
/// Bản ghi idempotency <c>(DeviceCode, IdempotencyKey)</c> không cứu được: nó chỉ hoạt động khi
/// có ĐỦ cả hai, nên đường legacy/simulator và các ca mất key vẫn rơi thẳng vào lỗi trên.
/// </para>
/// <para>
/// Ghi chú về nền test: EF InMemory cũng chặn khoá trùng (ném ngay lúc add/lưu), nên các test
/// dưới đây tái hiện đúng đường "trùng ⇒ ngoại lệ ⇒ 500"; chỉ khác kiểu ngoại lệ so với Npgsql.
/// </para>
/// </summary>
public class DuplicateTelemetryIngestTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"gh763-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            new AuditableEntityInterceptor(new CurrentUserService(new HttpContextAccessor())));

    private static BatchIngestSensorReadingsCommandHandler NewHandler(ApplicationDbContext db) =>
        new(new UnitOfWork(db),
            new NoopIotMetricsRecorder(),
            new NoopIotCalibrationCache(),
            new NoopTelemetryPublisher(),
            new NoopTelemetryStatsService(),
            NullLogger<BatchIngestSensorReadingsCommandHandler>.Instance);

    private static SensorReadingItem Item(Guid assetId, DateTime time, decimal voltage = 51.2m) => new()
    {
        Time = time,
        BatteryAssetId = assetId,
        Voltage = voltage,
        Current = 2m,
        Temperature = 28m,
        SocPercent = 80m,
    };

    private static async Task<(ApplicationDbContext Db, Guid AssetId)> SeedAssetAsync()
    {
        var db = NewDb();
        var asset = new BatteryAsset
        {
            Id = Guid.NewGuid(),
            SerialNumber = "BAT-2026-001",
            SiteId = Guid.NewGuid(),
        };
        db.BatteryAssets.Add(asset);
        await db.SaveChangesAsync();
        return (db, asset.Id);
    }

    [Fact]
    public async Task DuplicateOnlyBatch_IsSkipped_NotAnError()
    {
        var (db, assetId) = await SeedAssetAsync();
        await using var _ = db;
        var t = new DateTime(2026, 7, 27, 14, 7, 44, DateTimeKind.Utc);   // mốc trong issue

        var first = await NewHandler(db).Handle(
            new BatchIngestSensorReadingsCommand { Items = new() { Item(assetId, t) } }, default);
        first.Data!.Inserted.Should().Be(1);

        // Gửi lại y hệt — trước bản sửa: ngoại lệ khoá trùng ⇒ 500.
        var replay = await NewHandler(db).Handle(
            new BatchIngestSensorReadingsCommand { Items = new() { Item(assetId, t) } }, default);

        replay.IsSuccess.Should().BeTrue();
        replay.StatusCode.Should().Be(201);
        replay.Data!.Inserted.Should().Be(0);
        replay.Data.Skipped.Should().Be(1);
        replay.Data.TotalReceived.Should().Be(1);
        db.SensorReadings.Count().Should().Be(1, "gửi lại không được nhân đôi số đo");
    }

    [Fact]
    public async Task MixedBatch_KeepsNewReadings_AndSkipsOnlyTheDuplicate()
    {
        // ĐÂY là thiệt hại nặng nhất của lỗi cũ: một số đo trùng làm rollback cả các số đo MỚI.
        var (db, assetId) = await SeedAssetAsync();
        await using var _ = db;
        var t = new DateTime(2026, 7, 27, 14, 7, 44, DateTimeKind.Utc);

        await NewHandler(db).Handle(
            new BatchIngestSensorReadingsCommand { Items = new() { Item(assetId, t) } }, default);

        var mixed = await NewHandler(db).Handle(new BatchIngestSensorReadingsCommand
        {
            Items = new()
            {
                Item(assetId, t),                      // đã có
                Item(assetId, t.AddSeconds(5)),        // mới
                Item(assetId, t.AddSeconds(10)),       // mới
            }
        }, default);

        mixed.IsSuccess.Should().BeTrue();
        mixed.Data!.Inserted.Should().Be(2);
        mixed.Data.Skipped.Should().Be(1);
        db.SensorReadings.Count().Should().Be(3);
    }

    [Fact]
    public async Task DuplicateWithinTheSameBatch_IsSkipped_NotAnError()
    {
        // Hai item cùng (asset, time) trong CÙNG một request: EF ném ngay ở change tracker, cũng
        // ra 500 — chỉ khác thông báo. Dò trùng phải tính cả các item đã gặp trong chính batch.
        var (db, assetId) = await SeedAssetAsync();
        await using var _ = db;
        var t = new DateTime(2026, 7, 27, 14, 7, 44, DateTimeKind.Utc);

        var result = await NewHandler(db).Handle(new BatchIngestSensorReadingsCommand
        {
            Items = new()
            {
                Item(assetId, t, voltage: 51.2m),
                Item(assetId, t, voltage: 99.9m),   // trùng khoá, giá trị khác
                Item(assetId, t.AddSeconds(5)),
            }
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Inserted.Should().Be(2);
        result.Data.Skipped.Should().Be(1);
        db.SensorReadings.Count().Should().Be(2);
        // Giữ item ĐẦU TIÊN, không phải item sau ghi đè.
        db.SensorReadings.Single(r => r.Time == t).Voltage.Should().Be(51.2m);
    }

    [Fact]
    public async Task DuplicateAcrossDifferentAssets_AtTheSameInstant_IsNotADuplicate()
    {
        // Khoá là (time, asset) — hai pin khác nhau đo cùng một thời điểm là hoàn toàn bình thường.
        // Dò trùng chỉ theo thời gian sẽ nuốt mất số đo của pin thứ hai.
        var (db, assetA) = await SeedAssetAsync();
        await using var _ = db;
        var assetB = new BatteryAsset { Id = Guid.NewGuid(), SerialNumber = "BAT-2026-002", SiteId = Guid.NewGuid() };
        db.BatteryAssets.Add(assetB);
        await db.SaveChangesAsync();

        var t = new DateTime(2026, 7, 27, 14, 7, 44, DateTimeKind.Utc);
        var result = await NewHandler(db).Handle(new BatchIngestSensorReadingsCommand
        {
            Items = new() { Item(assetA, t), Item(assetB.Id, t) }
        }, default);

        result.Data!.Inserted.Should().Be(2);
        result.Data.Skipped.Should().Be(0);
    }

    [Fact]
    public async Task UnspecifiedKindTimestamp_MatchesTheStoredUtcRow()
    {
        // Simulator/legacy gửi mốc không kèm Kind. Handler chuẩn hoá về UTC khi GHI, nên phép DÒ
        // trùng phải dùng đúng phép chuẩn hoá đó — lệch nhau là dò hụt rồi vẫn dính lỗi lúc lưu.
        var (db, assetId) = await SeedAssetAsync();
        await using var _ = db;
        var utc = new DateTime(2026, 7, 27, 14, 7, 44, DateTimeKind.Utc);
        var unspecified = new DateTime(2026, 7, 27, 14, 7, 44, DateTimeKind.Unspecified);

        await NewHandler(db).Handle(
            new BatchIngestSensorReadingsCommand { Items = new() { Item(assetId, utc) } }, default);

        var replay = await NewHandler(db).Handle(
            new BatchIngestSensorReadingsCommand { Items = new() { Item(assetId, unspecified) } }, default);

        replay.Data!.Inserted.Should().Be(0);
        replay.Data.Skipped.Should().Be(1);
        db.SensorReadings.Count().Should().Be(1);
    }

    [Fact]
    public async Task Message_TellsDuplicatesApartFromOutliers()
    {
        // Trùng = thiết bị gửi lại (bình thường). Outlier = cảm biến đang hỏng (phải đi kiểm).
        // Gộp chung một câu thì người trực không biết có cần ra hiện trường hay không.
        var (db, assetId) = await SeedAssetAsync();
        await using var _ = db;
        var t = new DateTime(2026, 7, 27, 14, 7, 44, DateTimeKind.Utc);

        await NewHandler(db).Handle(
            new BatchIngestSensorReadingsCommand { Items = new() { Item(assetId, t) } }, default);

        var result = await NewHandler(db).Handle(new BatchIngestSensorReadingsCommand
        {
            Items = new()
            {
                Item(assetId, t),                                        // trùng
                Item(assetId, t.AddSeconds(5), voltage: 5000m),          // outlier (>1000V)
                Item(assetId, t.AddSeconds(10)),                         // hợp lệ
            }
        }, default);

        result.Data!.Inserted.Should().Be(1);
        result.Data.Skipped.Should().Be(2);
        result.Message.Should().Contain("outlier");
        result.Message.Should().Contain("already existed");
    }

    [Fact]
    public async Task CleanBatch_StillReportsPlainSuccess()
    {
        // Chống hồi quy: không trùng, không outlier ⇒ thông báo phải y như cũ.
        var (db, assetId) = await SeedAssetAsync();
        await using var _ = db;
        var t = new DateTime(2026, 7, 27, 14, 7, 44, DateTimeKind.Utc);

        var result = await NewHandler(db).Handle(new BatchIngestSensorReadingsCommand
        {
            Items = new() { Item(assetId, t), Item(assetId, t.AddSeconds(5)) }
        }, default);

        result.Data!.Inserted.Should().Be(2);
        result.Data.Skipped.Should().Be(0);
        result.Message.Should().Be("Sensor readings recorded successfully.");
    }
}
