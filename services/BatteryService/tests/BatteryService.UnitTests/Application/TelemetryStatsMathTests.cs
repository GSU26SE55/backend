using BatteryService.Application.Realtime;
using FluentAssertions;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Sprint Bonus NS-03 (#648) — logic thuần tính + merge min/max dòng nạp/xả.
/// </summary>
public class TelemetryStatsMathTests
{
    [Fact]
    public void ComputeBatch_SplitsChargeDischarge_AllPositive()
    {
        var s = TelemetryStatsMath.ComputeBatch(new[] { 2.0m, 0.5m, -4.0m, -1.0m });

        s.MaxChargeCurrent.Should().Be(2.0m);
        s.MinChargeCurrent.Should().Be(0.5m);
        s.ChargeSampleCount.Should().Be(2);
        s.MaxDischargeCurrent.Should().Be(4.0m, "MAX(ABS(current)) với current < 0");
        s.MinDischargeCurrent.Should().Be(1.0m);
        s.DischargeSampleCount.Should().Be(2);
    }

    [Fact]
    public void ComputeBatch_IdleZero_Ignored()
    {
        var s = TelemetryStatsMath.ComputeBatch(new[] { 0m, 0m });

        s.HasSamples.Should().BeFalse();
        s.MaxChargeCurrent.Should().BeNull();
        s.MaxDischargeCurrent.Should().BeNull();
        s.ChargeSampleCount.Should().Be(0);
        s.DischargeSampleCount.Should().Be(0);
    }

    [Fact]
    public void ComputeBatch_OnlyCharge_DischargeFieldsNull()
    {
        var s = TelemetryStatsMath.ComputeBatch(new[] { 1.0m, 2.0m });

        s.MaxChargeCurrent.Should().Be(2.0m);
        s.MaxDischargeCurrent.Should().BeNull();
        s.DischargeSampleCount.Should().Be(0);
    }

    [Fact]
    public void Merge_NewMaxLarger_Updates_NewMinSmaller_Updates()
    {
        var acc = new DirectionalStats(2.0m, 1.0m, 3.0m, 2.0m, 5, 5);
        var batch = new DirectionalStats(2.5m, 0.5m, 2.8m, 2.5m, 3, 3);

        var m = TelemetryStatsMath.Merge(acc, batch);

        m.MaxChargeCurrent.Should().Be(2.5m, "max mới lớn hơn → update");
        m.MinChargeCurrent.Should().Be(0.5m, "min mới nhỏ hơn → update");
        m.MaxDischargeCurrent.Should().Be(3.0m, "max mới nhỏ hơn → giữ cũ");
        m.MinDischargeCurrent.Should().Be(2.0m, "min mới lớn hơn → giữ cũ");
        m.ChargeSampleCount.Should().Be(8);
        m.DischargeSampleCount.Should().Be(8);
    }

    [Fact]
    public void Merge_AccumulatedEmpty_TakesBatch()
    {
        var m = TelemetryStatsMath.Merge(DirectionalStats.Empty, new DirectionalStats(1m, 1m, null, null, 2, 0));

        m.MaxChargeCurrent.Should().Be(1m);
        m.MaxDischargeCurrent.Should().BeNull();
        m.ChargeSampleCount.Should().Be(2);
    }

    [Fact]
    public void Merge_BatchDirectionAbsent_KeepsAccumulated()
    {
        var acc = new DirectionalStats(2m, 1m, 5m, 4m, 3, 3);
        var batch = new DirectionalStats(2.2m, 1.5m, null, null, 2, 0); // batch chỉ có nạp

        var m = TelemetryStatsMath.Merge(acc, batch);

        m.MaxDischargeCurrent.Should().Be(5m, "batch không có xả → giữ nguyên xả cũ");
        m.MinDischargeCurrent.Should().Be(4m);
        m.MaxChargeCurrent.Should().Be(2.2m);
    }
}
