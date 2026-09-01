using BatteryService.Application.CQRS.Command.Ambient;
using BatteryService.Application.CQRS.Handler.Ambient;
using BatteryService.Application.CQRS.Query.Ambient;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;
using SharedKernels.Interfaces;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Ba cảm biến ambient ghi ba hàng riêng cho cùng một chu kỳ đọc (DS18B20 chặn 750 ms nên rơi sau
/// khí gas ~1 s). Bảng lịch sử phải gộp chúng lại thành một dòng, nhưng KHÔNG được gộp lấn sang
/// chu kỳ kế tiếp.
/// </summary>
public class AmbientCycleMergeTests
{
    private static readonly Guid SiteId = Guid.NewGuid();

    private static AmbientReading Gas(DateTime t, decimal v) => new()
    { Time = t, SiteId = SiteId, GasConcentration = v, Source = AmbientReadingSourceEnum.IotSensor };

    private static AmbientReading Temp(DateTime t, decimal v) => new()
    { Time = t, SiteId = SiteId, AmbientTemperature = v, Source = AmbientReadingSourceEnum.IotSensor };

    private static AmbientReading Water(DateTime t, bool wet) => new()
    { Time = t, SiteId = SiteId, WaterLeakDetected = wet, Source = AmbientReadingSourceEnum.IotSensor };

    private static GetAmbientReadingHistoryQueryHandler HandlerOver(params AmbientReading[] rows)
    {
        var repo = new Mock<IGenericRepository<AmbientReading>>();
        repo.Setup(r => r.GetAllAsync()).Returns(rows.AsQueryable().BuildMock());
        var uow = new Mock<IBatteryUnitOfWork>();
        uow.SetupGet(u => u.AmbientReadings).Returns(repo.Object);
        return new GetAmbientReadingHistoryQueryHandler(uow.Object);
    }

    private static async Task<List<BatteryService.Application.DTOs.AmbientReadingDto>> HistoryOf(
        params AmbientReading[] rows)
    {
        var res = await HandlerOver(rows).Handle(
            new GetAmbientReadingHistoryQuery { SiteId = SiteId, PageNumber = 1, PageSize = 100 }, default);
        return res.Data!.Items;
    }

    [Fact]
    public async Task ThreeSensorsOfOneCycle_CollapseIntoSingleRow()
    {
        var t = new DateTime(2026, 8, 30, 10, 0, 0, DateTimeKind.Utc);

        var items = await HistoryOf(
            Temp(t.AddSeconds(1), 31.5m),   // DS18B20 chậm hơn ~1 s
            Gas(t, 64m),
            Water(t.AddSeconds(2), false));

        items.Should().ContainSingle("một chu kỳ đọc là một dòng");
        items[0].GasConcentration.Should().Be(64m);
        items[0].AmbientTemperature.Should().Be(31.5m);
        items[0].WaterLeakDetected.Should().BeFalse();
    }

    /// <summary>
    /// Chu kỳ gửi 15 s. Hai vòng liên tiếp phải là hai dòng — gộp nhầm thì biểu đồ mất một nửa số mẫu.
    /// </summary>
    [Fact]
    public async Task TwoCyclesFifteenSecondsApart_StayTwoRows()
    {
        var t = new DateTime(2026, 8, 30, 10, 0, 0, DateTimeKind.Utc);

        var items = await HistoryOf(
            Gas(t, 64m), Temp(t.AddSeconds(1), 31m),
            Gas(t.AddSeconds(15), 70m), Temp(t.AddSeconds(16), 32m));

        items.Should().HaveCount(2);
        items[0].GasConcentration.Should().Be(70m, "dòng mới nhất đứng trước");
        items[1].GasConcentration.Should().Be(64m);
    }

    /// <summary>
    /// Chốt cách chống nối dây chuyền: cụm được so với bản ghi MỚI NHẤT của nó, không phải bản liền
    /// trước. Nếu so với bản liền trước, một chuỗi đọc dày (mỗi 3 s) sẽ nối nhau vô hạn và nuốt trọn
    /// hàng giờ dữ liệu vào đúng một dòng.
    /// </summary>
    [Fact]
    public async Task DenseStream_DoesNotChainIntoOneGiantRow()
    {
        var t = new DateTime(2026, 8, 30, 10, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, 20).Select(i => Gas(t.AddSeconds(i * 3), 50m + i)).ToArray();

        var items = await HistoryOf(rows);

        items.Should().HaveCountGreaterThan(1, "20 mẫu trải 57 s không thể là một chu kỳ");
        items.Should().OnlyContain(x => x.GasConcentration != null);
    }

    [Fact]
    public async Task WeatherApiHourlyRows_AreNeverMergedTogether()
    {
        var t = new DateTime(2026, 8, 30, 10, 0, 0, DateTimeKind.Utc);

        var items = await HistoryOf(Temp(t, 30m), Temp(t.AddHours(-1), 29m));

        items.Should().HaveCount(2);
    }
}
