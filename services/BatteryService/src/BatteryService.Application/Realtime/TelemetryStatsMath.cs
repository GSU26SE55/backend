namespace BatteryService.Application.Realtime;

/// <summary>
/// Sprint Bonus NS-03 (#648) — logic thuần tính + merge min/max dòng nạp/xả (không IO → unit test dễ).
/// Quy ước: current &gt; 0 = nạp, current &lt; 0 = xả (min/max trả ABS dương), current == 0 (idle) bỏ qua.
/// </summary>
public static class TelemetryStatsMath
{
    /// <summary>Tính min/max/count 2 chiều cho tập dòng của 1 batch (đã lọc primary trước khi gọi).</summary>
    public static DirectionalStats ComputeBatch(IEnumerable<decimal> currents)
    {
        decimal? maxCharge = null, minCharge = null, maxDischarge = null, minDischarge = null;
        var chargeCount = 0;
        var dischargeCount = 0;

        foreach (var current in currents)
        {
            if (current > 0m)
            {
                chargeCount++;
                maxCharge = maxCharge is null ? current : Math.Max(maxCharge.Value, current);
                minCharge = minCharge is null ? current : Math.Min(minCharge.Value, current);
            }
            else if (current < 0m)
            {
                var abs = Math.Abs(current);
                dischargeCount++;
                maxDischarge = maxDischarge is null ? abs : Math.Max(maxDischarge.Value, abs);
                minDischarge = minDischarge is null ? abs : Math.Min(minDischarge.Value, abs);
            }
            // current == 0 (idle) không thuộc chiều nào.
        }

        return new DirectionalStats(maxCharge, minCharge, maxDischarge, minDischarge, chargeCount, dischargeCount);
    }

    /// <summary>Merge 2 snapshot (accumulated + batch mới): max giữ lớn hơn, min giữ nhỏ hơn, count cộng dồn.</summary>
    public static DirectionalStats Merge(DirectionalStats accumulated, DirectionalStats batch) => new(
        Extreme(accumulated.MaxChargeCurrent, batch.MaxChargeCurrent, keepLarger: true),
        Extreme(accumulated.MinChargeCurrent, batch.MinChargeCurrent, keepLarger: false),
        Extreme(accumulated.MaxDischargeCurrent, batch.MaxDischargeCurrent, keepLarger: true),
        Extreme(accumulated.MinDischargeCurrent, batch.MinDischargeCurrent, keepLarger: false),
        accumulated.ChargeSampleCount + batch.ChargeSampleCount,
        accumulated.DischargeSampleCount + batch.DischargeSampleCount);

    private static decimal? Extreme(decimal? a, decimal? b, bool keepLarger)
    {
        if (a is null)
            return b;
        if (b is null)
            return a;
        return keepLarger ? Math.Max(a.Value, b.Value) : Math.Min(a.Value, b.Value);
    }
}
