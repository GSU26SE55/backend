namespace BatteryService.Application.Realtime;

/// <summary>
/// Sprint Bonus NS-03 (#648) — snapshot min/max dòng tách 2 chiều nạp/xả cho 1 pin trong 1 window.
/// Giá trị dòng LUÔN dương (chiều nằm trong tên field); null = chưa có mẫu chiều đó.
/// </summary>
public sealed record DirectionalStats(
    decimal? MaxChargeCurrent,
    decimal? MinChargeCurrent,
    decimal? MaxDischargeCurrent,
    decimal? MinDischargeCurrent,
    int ChargeSampleCount,
    int DischargeSampleCount)
{
    public static readonly DirectionalStats Empty = new(null, null, null, null, 0, 0);

    public bool HasSamples => ChargeSampleCount > 0 || DischargeSampleCount > 0;
}
