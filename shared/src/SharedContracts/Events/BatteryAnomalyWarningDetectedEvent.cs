using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Sprint 6.2 NOTI-08 (#679) — bất thường pin mức KHÔNG-Critical (Warning / Info).
///
/// Vì sao là event RIÊNG chứ không tái dùng <see cref="BatteryAnomalyDetectedEvent"/>:
/// TicketService consume <c>BatteryAnomalyDetectedEvent</c> để auto-tạo ticket (BR-02) và
/// <c>AlertTicketSagaStateMachine</c> consume V2 cho cùng mục đích. Nếu publish severity Warning
/// lên chính event đó thì mọi cảnh báo nhẹ sẽ đẻ ticket — đúng nỗi lo "spam ticket" mà
/// <c>AnomalyDetectionService</c> đang né bằng cách không publish gì cả.
/// Event này CHỈ NotificationService consume (spec §3.4 T#11 Info → InApp, T#12 Warning → InApp+Push).
///
/// Chống spam: publisher dedup theo (BatteryAssetId, AnomalyType) trong cửa sổ
/// <c>Anomaly:WarningNotifyDedupMinutes</c> (mặc định 60') — xem <c>AnomalyDetectionService</c>.
/// </summary>
public record BatteryAnomalyWarningDetectedEvent(
    Guid AlertId,
    Guid? BatteryAssetId,
    Guid CustomerId,
    string? AssetSerialNumber,
    int AnomalyType,          // AnomalyTypeEnum value
    int Severity,             // AlertSeverityEnum value — Warning hoặc Info
    decimal? ThresholdValue,
    decimal? ActualValue,
    string? Unit,
    DateTime DetectedAt
) : IntegrationEvent;
