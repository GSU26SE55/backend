using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Sprint 5B B11 — publish khi BatteryService tạo mới BatteryAsset.
/// Subscribers: NotificationService (welcome push tới Customer), TicketService (cache map).
/// </summary>
public record BatteryAssetCreatedEvent(
    Guid BatteryAssetId,
    Guid CustomerId,
    Guid? SiteId,
    Guid BatteryTypeId,
    string SerialNumber,
    DateTime CreatedAt
) : IntegrationEvent;

/// <summary>
/// Sprint 5B B11 — publish khi BatteryAsset đổi chủ (transfer-owner).
/// Subscribers: NotificationService (gửi notify tới owner cũ + mới), TicketService (re-route).
/// </summary>
public record BatteryAssetTransferredEvent(
    Guid BatteryAssetId,
    Guid PreviousCustomerId,
    Guid NewCustomerId,
    Guid? PreviousSiteId,
    Guid? NewSiteId,
    string SerialNumber,
    DateTime TransferredAt,
    Guid PerformedByUserId
) : IntegrationEvent;
