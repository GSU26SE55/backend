namespace BatteryService.Application.Anomaly;

/// <summary>
/// Config cho anomaly engine. Bind từ <c>"AnomalyEngine"</c> section trong appsettings.
/// </summary>
public class AnomalyEngineOptions
{
    public const string SectionName = "AnomalyEngine";

    /// <summary>
    /// IOT3-80 — chu kỳ quét ngưỡng, hạ 30 → 10 giây.
    /// </summary>
    /// <remarks>
    /// Nếu mục tiêu là <b>phát hiện sự cố nhanh hơn</b> thì đây mới là con số có tác dụng, chứ
    /// không phải chu kỳ ĐO của thiết bị. Đo dày hơn chỉ làm dữ liệu vào nhanh hơn; cảnh báo vẫn
    /// phải đợi lượt quét kế tiếp. Với chu kỳ cũ 30 s, một sự cố xảy ra ngay sau lượt quét sẽ nằm
    /// im gần nửa phút dù số liệu đã có trong DB từ lâu.
    ///
    /// Và nó gần như <b>miễn phí</b>: quét là đọc bản ghi mới nhất mỗi pin, không ghi gì thêm —
    /// khác hẳn việc hạ chu kỳ đo, vốn nhân thẳng số dòng trong hypertable.
    /// </remarks>
    public int ScanIntervalSeconds { get; set; } = 10;
    public int DedupWindowMinutes { get; set; } = 30;
    public int OfflineThresholdMinutes { get; set; } = 10;
    public int EscalationAfterMinutes { get; set; } = 5;
    public int EscalationIntervalSeconds { get; set; } = 60;
    public int OutboxRelayIntervalSeconds { get; set; } = 5;
    public int OutboxRelayBatchSize { get; set; } = 100;

    /// <summary>
    /// Sprint 5B B10 (#158) — auto-resolve Open alerts khi anomaly không còn xuất hiện
    /// trong cửa sổ lookback.
    /// </summary>
    public int AutoResolveIntervalSeconds { get; set; } = 300;
    public int AutoResolveLookbackMinutes { get; set; } = 10;
    public int AutoResolveBatchSize { get; set; } = 100;

    /// <summary>
    /// Sprint 7 B4 (§31.7) — chu kỳ recompute cascade risk (mặc định 5 phút).
    /// </summary>
    public int CascadeRiskIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Sprint 6.2 NOTI-08 (#679) — bật publish <c>BatteryAnomalyWarningDetectedEvent</c> cho alert
    /// mức Warning/Info để NotificationService báo Customer (spec §3.4 T#11/T#12).
    /// Event này KHÔNG sinh ticket (TicketService không consume) nên không lặp lại nỗi lo spam ticket.
    /// Đặt false để quay lại hành vi cũ (chỉ ghi alert, không báo ai).
    /// </summary>
    public bool PublishWarningNotifications { get; set; } = true;

    /// <summary>
    /// Sprint 6.2 NOTI-08 (#679) — cửa sổ chống spam notify Warning/Info, tính theo
    /// (BatteryAssetId × AnomalyType). Mặc định 60 phút: pin chớm vượt ngưỡng có thể nhấp nháy
    /// liên tục nhiều tick, nhưng Customer chỉ cần biết 1 lần mỗi giờ.
    /// </summary>
    public int WarningNotifyDedupMinutes { get; set; } = 60;
}
