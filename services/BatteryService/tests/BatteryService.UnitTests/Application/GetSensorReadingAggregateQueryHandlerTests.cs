using BatteryService.Application.CQRS.Handler.SensorReading;
using BatteryService.Application.CQRS.Query.SensorReading;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Sprint Bonus NS-02 (#647) — /aggregate min/max nạp/xả tách chiều + V/T, lọc source primary,
/// trả dương/nullable (newsprint.md §2 + test plan §6).
/// </summary>
public class GetSensorReadingAggregateQueryHandlerTests
{
    private static readonly Guid AssetId = Guid.NewGuid();
    private static readonly DateTime Bucket = new(2026, 7, 8, 9, 0, 0, DateTimeKind.Utc);

    private static SensorReading Reading(
        decimal current,
        decimal voltage = 52m,
        decimal temperature = 28m,
        string? sourceCode = "primary",
        int secondOffset = 0) => new()
        {
            Time = Bucket.AddSeconds(secondOffset),
            BatteryAssetId = AssetId,
            Voltage = voltage,
            Current = current,
            Temperature = temperature,
            SocPercent = 70m,
            SensorSourceCode = sourceCode
        };

    private static GetSensorReadingAggregateQueryHandler Sut(MockUnitOfWorkBuilder b) => new(b.Build(), TestBatteryCurrentUserService.Admin());

    private static GetSensorReadingAggregateQuery Query() => new()
    {
        BatteryAssetId = AssetId,
        Interval = "1h"
    };

    [Fact]
    public async Task Bucket_WithChargeAndDischarge_SplitsCorrectly_AllPositive()
    {
        var b = new MockUnitOfWorkBuilder().WithSensorReadings(
            Reading(current: 2.0m, secondOffset: 0),   // nạp
            Reading(current: 0.5m, secondOffset: 5),   // nạp
            Reading(current: -4.0m, secondOffset: 10), // xả
            Reading(current: -1.0m, secondOffset: 15));// xả

        var res = await Sut(b).Handle(Query(), CancellationToken.None);

        res.IsSuccess.Should().BeTrue();
        res.Data.Should().HaveCount(1);
        var bucket = res.Data![0];

        bucket.MaxChargeCurrent.Should().Be(2.0m);
        bucket.MinChargeCurrent.Should().Be(0.5m);
        bucket.AvgChargeCurrent.Should().Be(1.25m);
        bucket.ChargeSampleCount.Should().Be(2);

        bucket.MaxDischargeCurrent.Should().Be(4.0m, "trả dương: MAX(ABS(current)) với current < 0");
        bucket.MinDischargeCurrent.Should().Be(1.0m);
        bucket.AvgDischargeCurrent.Should().Be(2.5m);
        bucket.DischargeSampleCount.Should().Be(2);
    }

    [Fact]
    public async Task Bucket_OnlyIdle_ChargeDischargeFieldsNull()
    {
        var b = new MockUnitOfWorkBuilder().WithSensorReadings(
            Reading(current: 0m, secondOffset: 0),
            Reading(current: 0m, secondOffset: 5));

        var res = await Sut(b).Handle(Query(), CancellationToken.None);
        var bucket = res.Data!.Single();

        bucket.MaxChargeCurrent.Should().BeNull("0A idle không thuộc chiều nào → không phải 0");
        bucket.MinChargeCurrent.Should().BeNull();
        bucket.AvgChargeCurrent.Should().BeNull();
        bucket.MaxDischargeCurrent.Should().BeNull();
        bucket.MinDischargeCurrent.Should().BeNull();
        bucket.AvgDischargeCurrent.Should().BeNull();
        bucket.ChargeSampleCount.Should().Be(0);
        bucket.DischargeSampleCount.Should().Be(0);
    }

    [Fact]
    public async Task Bucket_ExcludesNonPrimaryReadings()
    {
        var b = new MockUnitOfWorkBuilder().WithSensorReadings(
            Reading(current: 2.0m, voltage: 52m, sourceCode: "primary", secondOffset: 0),
            Reading(current: 0.05m, voltage: 99m, sourceCode: "redundant", secondOffset: 1),   // INA226 noise
            Reading(current: 2.0m, voltage: 0m, sourceCode: "external-temp", secondOffset: 2)); // DS18B20 mirror

        var res = await Sut(b).Handle(Query(), CancellationToken.None);
        var bucket = res.Data!.Single();

        bucket.ChargeSampleCount.Should().Be(1, "chỉ đếm reading primary — redundant/external-temp bị loại");
        bucket.MaxVoltage.Should().Be(52m, "không dính voltage 99V của redundant hay 0V của external-temp");
        bucket.MaxChargeCurrent.Should().Be(2.0m);
    }

    [Fact]
    public async Task Bucket_NullSourceCode_TreatedAsPrimary()
    {
        var b = new MockUnitOfWorkBuilder().WithSensorReadings(
            Reading(current: 3.0m, sourceCode: null, secondOffset: 0));

        var res = await Sut(b).Handle(Query(), CancellationToken.None);

        res.Data!.Single().ChargeSampleCount.Should().Be(1);
    }

    [Fact]
    public async Task Bucket_MinMaxVoltageTemperature_Computed()
    {
        var b = new MockUnitOfWorkBuilder().WithSensorReadings(
            Reading(current: 1m, voltage: 51.9m, temperature: 27.1m, secondOffset: 0),
            Reading(current: 1m, voltage: 52.8m, temperature: 30.2m, secondOffset: 5));

        var res = await Sut(b).Handle(Query(), CancellationToken.None);
        var bucket = res.Data!.Single();

        bucket.MinVoltage.Should().Be(51.9m);
        bucket.MaxVoltage.Should().Be(52.8m);
        bucket.MinTemperature.Should().Be(27.1m);
        bucket.MaxTemperature.Should().Be(30.2m);
    }

    [Fact]
    public async Task Bucket_KeepsBackwardCompatAvgFields()
    {
        var b = new MockUnitOfWorkBuilder().WithSensorReadings(
            Reading(current: 2.0m, secondOffset: 0),
            Reading(current: -4.0m, secondOffset: 10));

        var res = await Sut(b).Handle(Query(), CancellationToken.None);
        var bucket = res.Data!.Single();

        bucket.AvgCurrent.Should().Be(-1.0m, "AvgCurrent trộn dấu giữ backward-compat: (2 + -4)/2");
    }

    [Fact]
    public async Task NoReadings_ReturnsEmptyList()
    {
        var res = await Sut(new MockUnitOfWorkBuilder()).Handle(Query(), CancellationToken.None);

        res.IsSuccess.Should().BeTrue();
        res.Data.Should().BeEmpty();
    }
}
