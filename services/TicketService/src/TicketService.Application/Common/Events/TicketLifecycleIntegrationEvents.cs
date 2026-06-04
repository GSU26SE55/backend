using SharedContracts.Events.Root;
using TicketService.Domain.Enums;

namespace TicketService.Application.Common.Events;

public record TicketCreatedIntegrationEvent(Guid TicketId, string Code) : IntegrationEvent;

public record TicketAssignedIntegrationEvent(Guid TicketId, string Code, Guid StaffId, string Priority) : IntegrationEvent;

public record TicketResolvedIntegrationEvent(Guid TicketId, string Code, Guid StaffId, string ResolutionSummary) : IntegrationEvent;

public record TicketApprovedIntegrationEvent(Guid TicketId, string Code, Guid CustomerId) : IntegrationEvent;

public record TicketRejectedIntegrationEvent(Guid TicketId, string Code, Guid StaffId, string Reason) : IntegrationEvent;

public record TicketStatusChangedIntegrationEvent(Guid TicketId, string Code, TicketStatusEnum OldStatus, TicketStatusEnum NewStatus) : IntegrationEvent;

public record TicketReopenedIntegrationEvent(Guid TicketId, string Code, Guid CustomerId, string ReopenReason) : IntegrationEvent;

public record TicketRatedIntegrationEvent(Guid TicketId, string Code, Guid CustomerId, short Rating, string? RatingComment) : IntegrationEvent;

public record TicketEscalatedIntegrationEvent(Guid TicketId, string Code, EscalationReasonEnum Reason, string? Note, Guid? StaffId, string? StaffName) : IntegrationEvent;
