using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

public class OutboxLeaseOwner : IOutboxLeaseOwner
{
    public string Value { get; } = $"ticket-relay-{Environment.MachineName}-{Guid.NewGuid():N}";
}
