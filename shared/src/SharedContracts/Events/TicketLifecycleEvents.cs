using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Publish khi Ticket mới được tạo (thủ công hoặc auto từ BatteryAnomalyDetected).
/// Subscribers: NotificationService (notify Manager).
/// </summary>
public record TicketCreatedEvent(
    Guid TicketId,
    string Code
) : IntegrationEvent;

/// <summary>
/// Publish khi Manager assign hoặc reassign Staff vào Ticket.
/// Subscribers: NotificationService (notify Staff được assign).
/// </summary>
public record TicketAssignedEvent(
    Guid TicketId,
    string Code,
    Guid StaffId,
    string Priority
) : IntegrationEvent;

/// <summary>
/// Publish khi Staff resolve Ticket.
/// Subscribers: NotificationService (notify Customer + Manager).
/// </summary>
public record TicketResolvedEvent(
    Guid TicketId,
    string Code,
    Guid StaffId,
    string ResolutionSummary
) : IntegrationEvent;

/// <summary>
/// Publish khi Ticket bị escalate (SLA breach hoặc Staff/Manager request).
/// Reason là int (serialize từ EscalationReasonEnum) để tránh reference TicketService.Domain.
/// Subscribers: NotificationService (notify Manager + Admin).
/// </summary>
public record TicketEscalatedEvent(
    Guid TicketId,
    string Code,
    int Reason,
    string? Note,
    Guid? StaffId,
    string? StaffName
) : IntegrationEvent;

/// <summary>
/// Publish khi Manager declare P1 Critical Incident từ Ticket.
/// Subscribers: NotificationService (notify Manager + Admin).
/// </summary>
public record IncidentDeclaredEvent(
    Guid TicketId,
    string Code,
    Guid ManagerId
) : IntegrationEvent;
