using TicketService.Domain.Entities;

namespace TicketService.Application.Interfaces.Services;

public interface IOutboxClaimService
{
    Task<OutboxMessage?> TryClaimAsync(Guid messageId, string leaseOwner, TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<bool> MarkProcessedAsync(Guid messageId, string leaseOwner, CancellationToken cancellationToken = default);

    Task<bool> MarkFailedAsync(Guid messageId, string leaseOwner, string error,
        CancellationToken cancellationToken = default);
}
