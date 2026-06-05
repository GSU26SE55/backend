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
    System = 99
}
