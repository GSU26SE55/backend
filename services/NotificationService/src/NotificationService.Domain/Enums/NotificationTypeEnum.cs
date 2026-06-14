namespace NotificationService.Domain.Enums;

/// <summary>
/// Loại notification — phục vụ template routing và filter UI.
/// Bám theo §3.3 overall.md (NotificationService).
/// </summary>
public enum NotificationTypeEnum
{
    TicketCreated = 1,
    TicketAssigned = 2,
    TicketStatusChanged = 3,
    TicketResolved = 4,
    TicketClosed = 5,
    TicketEscalated = 6,
    SlaWarning = 7,
    SlaBreached = 8,
    BatteryAnomalyDetected = 9,
    EnvironmentalIncidentDetected = 10,
    EnvironmentalIncidentResolved = 11,
    AccountActivated = 12,
    AdminInvite = 13,
    IncidentDeclared = 14,

    /// <summary>
    /// Sprint 5B #238 — Critical Alert chưa-ack > 5 phút
    /// (<c>BatteryAlertEscalationRequestedEvent</c>).
    /// </summary>
    BatteryAlertEscalationPending = 16,

    /// <summary>
    /// Sprint 5B #238 — Alert–Ticket Saga vào terminal state Failed,
    /// admin reprocess required (<c>AlertTicketSagaFailedEvent</c>).
    /// </summary>
    AlertTicketSagaFailed = 17,

    /// <summary>Sprint IoT-1 (#249) — IoT edge device mất heartbeat &gt; 5 phút.</summary>
    IotDeviceWentOffline = 18,

    System = 99
}
