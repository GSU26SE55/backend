using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Sprint 7 B4 (§31.7) — publish khi <c>CascadeRiskScore</c> của một asset vượt ngưỡng cao
/// (&gt;= 0.7) sau khi <c>CascadeRiskBackgroundService</c> recompute.
///
/// Trigger: rủi ro lan truyền cao → 1 pin hỏng có thể kéo theo pin lân cận cùng site.
///
/// Subscribers:
/// - TicketService: upgrade Priority ticket liên quan lên P1 (auto) — override ImpactScope
///   lên ít nhất Site qua Priority Matrix (§2.4bis).
/// - NotificationService: notify Manager dashboard.
/// </summary>
public record BatteryCascadeRiskHighEvent(
    Guid BatteryAssetId,
    Guid? SiteId,
    Guid CustomerId,
    string AssetSerialNumber,
    decimal CascadeRiskScore,   // 0.0–1.0
    Guid? RelatedTicketId,      // ticket đang Open của asset (nếu có) để upgrade Priority
    DateTime DetectedAt
) : IntegrationEvent;
