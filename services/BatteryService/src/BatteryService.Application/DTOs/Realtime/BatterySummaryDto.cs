namespace BatteryService.Application.DTOs.Realtime;

/// <summary>
/// Sprint BE-IoT-Realtime (#618) — payload event SSE <c>summary</c> (§34.10.5).
/// Gom giá trị mới nhất MỖI pin trong scope multi-asset (assets/customer/site/sites/type/all/site:none),
/// throttle ~3–5s (chống flood nhiều pin).
///
/// <para>
/// <b>Đầy đủ thông số (#parity với event <c>reading</c>):</b> mỗi item là 1 <see cref="LiveReadingDto"/>
/// HOÀN CHỈNH (voltage, current, temperature, soc, soh, cycle, chargingState, internalResistance,
/// cellVoltageDelta, bmsErrorCode, sourceType, sensorSourceCode, ...) — KHÔNG còn rút gọn vài field.
/// Dùng lại <c>LiveReadingDto</c> để parity tự động (thêm field vào reading là summary có ngay,
/// không phải sửa 2 nơi → tránh thiếu sót).
/// </para>
/// <para>
/// <b>Đa nguồn:</b> coalescer ưu tiên giữ source <c>primary</c> (BMS — đủ thông số) mỗi pin, tránh
/// hiển thị số liệu một phần của <c>redundant</c>/<c>external-temp</c> (V/I hoặc temp = 0). Xem
/// <c>RedisTelemetryStream</c>.
/// </para>
/// </summary>
public class BatterySummaryDto
{
    /// <summary>"asset" | "customer" | "site" | "type" | "all" | "site:none" — khớp keyword scope.</summary>
    public string ScopeType { get; set; } = string.Empty;

    /// <summary>Mỗi pin 1 item — payload đầy đủ y hệt event <c>reading</c>.</summary>
    public List<LiveReadingDto> Items { get; set; } = new();
}
