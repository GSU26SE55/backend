namespace BatteryService.Application.Services;

/// <summary>
/// Sprint IoT-2 #IoT2-28 (S6-BE-02) — cross-source mismatch detection.
///
/// Khi insert reading mới, query reading cùng <c>BatteryAssetId</c> trong cửa sổ 60s
/// ở <c>SourceType</c> khác (Bms vs IotGateway). Lệch V &gt; 0.5V hoặc T &gt; 5°C
/// → tạo <c>Alert(SensorMismatch, Warning)</c>.
/// </summary>
public interface ICrossSourceValidationService
{
    /// <summary>
    /// Scan reading mới insert trong khoảng [<paramref name="since"/>, now]. Trả số alert tạo ra.
    /// </summary>
    Task<int> ScanRecentReadingsAsync(DateTime since, CancellationToken cancellationToken = default);
}
