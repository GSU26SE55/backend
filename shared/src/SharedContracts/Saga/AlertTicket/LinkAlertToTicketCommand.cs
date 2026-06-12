using SharedContracts.Events.Root;

namespace SharedContracts.Saga.AlertTicket;

/// <summary>
/// Saga → BatteryService: update Alert.TicketId trỏ về Ticket đã tạo.
/// Sent sau khi nhận <see cref="TicketCreatedFromAlertResponse"/>.
///
/// Idempotent: nếu Alert.TicketId đã được set bằng giá trị mong đợi,
/// BatteryService trả về <see cref="AlertLinkedToTicketResponse"/> ngay
/// (không update lại).
///
/// Sprint 5B #236 — Saga contracts.
/// </summary>
public record LinkAlertToTicketCommand(
    Guid CorrelationId,
    Guid AlertId,
    Guid TicketId,
    string TicketCode
) : IntegrationEvent;
