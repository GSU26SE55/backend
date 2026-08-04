using SharedContracts.Events.Root;

namespace SharedContracts.Events;

public record TicketMergedEvent(
    Guid SourceTicketId,
    string SourceTicketCode,
    Guid SourceCustomerId,
    Guid MasterTicketId,
    string MasterTicketCode,
    Guid MergedByManagerId) : IntegrationEvent;
