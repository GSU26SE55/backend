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
