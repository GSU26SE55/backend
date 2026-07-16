namespace BatteryService.Application.Common;

/// <summary>
/// Sprint Bonus (#647/#648/#652/#653) — quy ước phân loại nguồn <c>SensorReading.SensorSourceCode</c>.
/// Một pin có thể có nhiều reading cùng timestamp từ các nguồn khác nhau (§52.9):
/// <c>primary</c> (BMS, đầy đủ thông số) · <c>redundant</c> (INA226, chỉ V/I) ·
/// <c>external-temp</c> (DS18B20, mirror current của BMS).
///
/// Mọi phép tính/hiển thị mặc định CHỈ lấy reading <c>primary</c>; nếu không lọc, mỗi giá trị bị
/// nhân ~3 lần và dính noise của nguồn phụ. Quy ước dùng CHUNG với coalescer SSE
/// (<c>RedisTelemetryStream.IsPrimary</c>) để không lệch giữa các tầng.
/// </summary>
public static class SensorSource
{
    public const string Primary = "primary";
    public const string Redundant = "redundant";
    public const string ExternalTemp = "external-temp";

    /// <summary>
    /// True nếu reading là nguồn chính. Thiết bị single-source không gắn tag (null/empty) cũng
    /// coi như primary — cùng quy ước coalescer SSE.
    /// </summary>
    public static bool IsPrimary(string? sensorSourceCode) =>
        string.IsNullOrEmpty(sensorSourceCode) ||
        string.Equals(sensorSourceCode, Primary, StringComparison.OrdinalIgnoreCase);
}
