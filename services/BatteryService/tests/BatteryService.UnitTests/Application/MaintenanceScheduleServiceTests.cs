using BatteryService.Application.Common.Models;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Nhật ký bảo trì định kỳ: đến hạn thì ghi một mốc kèm ảnh chụp tình trạng pin, rồi dời
/// lịch sang kỳ kế tiếp.
/// </summary>
public class MaintenanceScheduleServiceTests
{
    private static readonly DateTime NowUtc = new(2027, 3, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid AssetId = Guid.Parse("aaaa0001-0000-4000-8000-000000000024");

    private static MaintenanceScheduleOptions Options(
        int defaultCycleMonths = 6,
        int leadDays = 7) => new()
        {
            Enabled = true,
            DefaultCycleMonths = defaultCycleMonths,
            LeadDays = leadDays,
            BatchSize = 100
        };

    private static BatteryAsset Asset(
        DateTime nextDue,
        DateTime? lastMaintenance = null,
        int cycleNo = 1,
        int? intervalMonths = null,
        BatteryStatusEnum status = BatteryStatusEnum.Active) => new()
        {
            Id = AssetId,
            SerialNumber = "BAT-TEST-001",
            Status = status,
            NextMaintenanceDueAtUtc = nextDue,
            LastMaintenanceAtUtc = lastMaintenance,
            MaintenanceCycleNo = cycleNo,
            BatteryType = new BatteryType
            {
                Name = "Test type",
                MaintenanceIntervalMonths = intervalMonths
            }
        };

    private static (MaintenanceScheduleService service, MockUnitOfWorkBuilder mocks,
        List<MaintenanceCycle> written, List<IntegrationEvent> published) Build(
        BatteryAsset[] assets,
        SensorReading[]? readings = null,
        Alert[]? alerts = null,
        MaintenanceScheduleOptions? options = null)
    {
        var mocks = new MockUnitOfWorkBuilder();
        mocks.BatteryAssets.Setup(r => r.GetAllAsync()).Returns(assets.AsQueryable().BuildMock());
        mocks.SensorReadings.Setup(r => r.GetAllAsync())
            .Returns((readings ?? []).AsQueryable().BuildMock());
        mocks.Alerts.Setup(r => r.GetAllAsync()).Returns((alerts ?? []).AsQueryable().BuildMock());
        mocks.MaintenanceCycles.Setup(r => r.GetAllAsync())
            .Returns(Array.Empty<MaintenanceCycle>().AsQueryable().BuildMock());

        var written = new List<MaintenanceCycle>();
        mocks.MaintenanceCycles
            .Setup(r => r.AddAsync(It.IsAny<MaintenanceCycle>()))
            .Callback<MaintenanceCycle>(written.Add)
            .Returns(Task.CompletedTask);

        // Ghi lại event phát ra: TicketService dựa vào nó để mở ticket bảo trì, nên nó là
        // một phần hành vi của service này chứ không phải chi tiết hạ tầng.
        var published = new List<IntegrationEvent>();
        var outbox = new Mock<IIntegrationEventOutboxWriter>();
        outbox.Setup(w => w.WriteAsync(It.IsAny<MaintenanceCycleDueEvent>(), It.IsAny<CancellationToken>()))
            .Callback<MaintenanceCycleDueEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        var service = new MaintenanceScheduleService(
            mocks.UnitOfWork.Object,
            Microsoft.Extensions.Options.Options.Create(options ?? Options()),
            outbox.Object,
            Mock.Of<ILogger<MaintenanceScheduleService>>());

        return (service, mocks, written, published);
    }

    [Fact]
    public async Task RecordDueCycles_WhenNotYetDue_WritesNothing()
    {
        var asset = Asset(nextDue: NowUtc.AddDays(30));
        var (service, _, written, _) = Build([asset]);

        var count = await service.RecordDueCyclesAsync(NowUtc, CancellationToken.None);

        count.Should().Be(0);
        written.Should().BeEmpty();
        asset.MaintenanceCycleNo.Should().Be(1);
    }

    [Fact]
    public async Task RecordDueCycles_WhenInsideLeadWindow_WritesCycleAndPublishesEvent()
    {
        var dueAt = NowUtc.AddDays(7).AddHours(1);
        var asset = Asset(nextDue: dueAt);
        var (service, _, written, published) = Build(
            [asset],
            options: Options(leadDays: 7));

        var count = await service.RecordDueCyclesAsync(NowUtc, CancellationToken.None);

        count.Should().Be(1);
        written.Should().ContainSingle().Which.DueAtUtc.Should().Be(dueAt);
        published.OfType<MaintenanceCycleDueEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task RecordDueCycles_WhenNextLocalDayIsOutsideLeadWindow_WritesNothing()
    {
        // NowUtc = 15:00 Asia/Ho_Chi_Minh. +7d10h falls at 01:00 on the eighth local day.
        var asset = Asset(nextDue: NowUtc.AddDays(7).AddHours(10));
        var (service, _, written, published) = Build(
            [asset],
            options: Options(leadDays: 7));

        var count = await service.RecordDueCyclesAsync(NowUtc, CancellationToken.None);

        count.Should().Be(0);
        written.Should().BeEmpty();
        published.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordDueCycles_WhenDue_WritesCycleAndAdvancesSchedule()
    {
        var dueAt = NowUtc.AddHours(-2);
        var asset = Asset(nextDue: dueAt, cycleNo: 3);
        var (service, mocks, written, _) = Build([asset]);

        var count = await service.RecordDueCyclesAsync(NowUtc, CancellationToken.None);

        count.Should().Be(1);
        written.Should().ContainSingle();
        written[0].CycleNo.Should().Be(3);
        written[0].DueAtUtc.Should().Be(dueAt);
        written[0].RecordedAtUtc.Should().Be(NowUtc);

        // Kỳ kế tiếp tính từ HẠN KẾ HOẠCH, không phải từ lúc worker chạy — worker trễ vài
        // giờ thì lịch vẫn phải đều đặn 6 tháng một lần, không trượt dần.
        asset.LastMaintenanceAtUtc.Should().Be(dueAt);
        asset.NextMaintenanceDueAtUtc.Should().Be(dueAt.AddMonths(6));
        asset.MaintenanceCycleNo.Should().Be(4);
        mocks.UnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Once);
    }

    [Fact]
    public async Task RecordDueCycles_UsesBatteryTypeInterval_OverSystemDefault()
    {
        var dueAt = NowUtc.AddHours(-1);
        // Loại pin khai 12 tháng — chu kỳ phải theo loại pin, không dùng mặc định 6 tháng.
        var asset = Asset(nextDue: dueAt, intervalMonths: 12);
        var (service, _, _, _) = Build([asset], options: Options(defaultCycleMonths: 6));

        await service.RecordDueCyclesAsync(NowUtc, CancellationToken.None);

        asset.NextMaintenanceDueAtUtc.Should().Be(dueAt.AddMonths(12));
    }

    [Fact]
    public async Task RecordDueCycles_SkipsNonActiveAssets()
    {
        // Pin đã ngừng vận hành thì không cần theo dõi định kỳ nữa.
        var asset = Asset(nextDue: NowUtc.AddDays(-10), status: BatteryStatusEnum.Decommissioned);
        var (service, _, written, _) = Build([asset]);

        var count = await service.RecordDueCyclesAsync(NowUtc, CancellationToken.None);

        count.Should().Be(0);
        written.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordDueCycles_CapturesSnapshotFromReadingsAndAlerts()
    {
        var periodStart = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var dueAt = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var asset = Asset(nextDue: dueAt, lastMaintenance: periodStart, cycleNo: 2);

        SensorReading Reading(DateTime at, decimal temp, decimal volt, int cycles, decimal? soh) => new()
        {
            BatteryAssetId = AssetId,
            Time = at,
            Temperature = temp,
            Voltage = volt,
            Current = 2m,
            SocPercent = 60m,
            CycleCount = cycles,
            SohPercent = soh
        };

        var readings = new[]
        {
            Reading(periodStart.AddDays(1), 30m, 26m, 100, 92m),
            Reading(periodStart.AddDays(60), 40m, 25m, 120, 91m),
            // Bản ghi mới nhất trong kỳ — SoH phải lấy từ đây, không phải trung bình.
            Reading(dueAt.AddDays(-1), 50m, 28m, 150, 90m),
            // Ngoài khoảng kỳ — không được tính vào.
            Reading(dueAt.AddDays(5), 99m, 30m, 999, 10m),
            Reading(periodStart.AddDays(-5), 5m, 20m, 1, 99m)
        };

        var alerts = new[]
        {
            new Alert { BatteryAssetId = AssetId, DetectedAt = periodStart.AddDays(2), Severity = AlertSeverityEnum.Warning },
            new Alert { BatteryAssetId = AssetId, DetectedAt = periodStart.AddDays(3), Severity = AlertSeverityEnum.Critical },
            // Ngoài khoảng kỳ.
            new Alert { BatteryAssetId = AssetId, DetectedAt = dueAt.AddDays(10), Severity = AlertSeverityEnum.Critical }
        };

        var (service, _, written, _) = Build([asset], readings, alerts);

        await service.RecordDueCyclesAsync(NowUtc, CancellationToken.None);

        var cycle = written.Should().ContainSingle().Subject;
        cycle.ReadingCount.Should().Be(3);
        cycle.SohPercentAtCycle.Should().Be(90m);
        cycle.AvgTemperatureCelsius.Should().Be(40m);
        cycle.MaxTemperatureCelsius.Should().Be(50m);
        cycle.MinVoltage.Should().Be(25m);
        cycle.MaxVoltage.Should().Be(28m);
        cycle.CycleCountDelta.Should().Be(50);
        cycle.AlertCount.Should().Be(2);
        cycle.CriticalAlertCount.Should().Be(1);
    }

    [Fact]
    public async Task RecordDueCycles_WithNoReadings_StillRecordsCycle()
    {
        // Pin mất kết nối cả kỳ vẫn phải ghi được mốc — thiếu dữ liệu không được chặn
        // nhật ký, nếu không lịch sử sẽ thủng một kỳ.
        var asset = Asset(nextDue: NowUtc.AddHours(-1));
        var (service, _, written, _) = Build([asset]);

        await service.RecordDueCyclesAsync(NowUtc, CancellationToken.None);

        var cycle = written.Should().ContainSingle().Subject;
        cycle.ReadingCount.Should().Be(0);
        cycle.SohPercentAtCycle.Should().BeNull();
        cycle.AvgTemperatureCelsius.Should().BeNull();
    }


    // ---------- phát sự kiện cho TicketService ----------

    /// <summary>
    /// Ghi nhật ký thôi thì không ai được cử đi. Sự kiện này là thứ khiến TicketService mở
    /// ticket bảo trì, nhờ đó công việc quay lại hàng chờ của Manager cùng SLA và phân công.
    /// Thiếu nó, hệ thống biết pin tới kỳ, ghi lại sức khoẻ, rồi im lặng.
    /// </summary>
    [Fact]
    public async Task RecordDueCycles_WhenDue_PublishesTheEventTicketServiceNeeds()
    {
        var dueAt = NowUtc.AddDays(-1);
        var asset = Asset(nextDue: dueAt, cycleNo: 4, intervalMonths: 9);
        var (service, _, written, published) = Build([asset]);

        await service.RecordDueCyclesAsync(NowUtc, CancellationToken.None);

        var evt = published.OfType<MaintenanceCycleDueEvent>().Should().ContainSingle().Subject;
        evt.BatteryAssetId.Should().Be(AssetId);
        evt.SerialNumber.Should().Be("BAT-TEST-001");
        evt.CycleNo.Should().Be(4, "phải là số kỳ vừa ghi, không phải kỳ kế tiếp");
        evt.DueAtUtc.Should().Be(dueAt);
        evt.IntervalMonths.Should().Be(9);

        // Sự kiện phải trỏ đúng dòng nhật ký vừa ghi để truy ngược được từ ticket.
        evt.MaintenanceCycleId.Should().Be(written.Should().ContainSingle().Subject.Id);
    }

    /// <summary>
    /// Id tất định theo (pin, hạn kỳ): worker chạy lại hoặc hai replica cùng chạy thì
    /// TicketService vẫn nhận ra là một lần, nên không mở hai ticket cho cùng một kỳ.
    /// </summary>
    [Fact]
    public async Task TheEventId_IsDerivedFromTheBatteryAndDueDate()
    {
        var dueAt = NowUtc.AddDays(-1);
        var (service, _, _, published) = Build([Asset(nextDue: dueAt)]);

        await service.RecordDueCyclesAsync(NowUtc, CancellationToken.None);

        var evt = published.OfType<MaintenanceCycleDueEvent>().Single();
        evt.Id.Should().Be(DeterministicEventId.From(AssetId, $"maintenance-cycle-due:{dueAt:O}"));
    }

    /// <summary>Pin chưa tới kỳ thì không ghi gì và cũng không báo ai.</summary>
    [Fact]
    public async Task RecordDueCycles_WhenNotYetDue_PublishesNothing()
    {
        var (service, _, _, published) = Build([Asset(nextDue: NowUtc.AddDays(30))]);

        await service.RecordDueCyclesAsync(NowUtc, CancellationToken.None);

        published.Should().BeEmpty();
    }
}
