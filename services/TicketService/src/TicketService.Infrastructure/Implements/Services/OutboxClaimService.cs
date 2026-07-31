using Microsoft.EntityFrameworkCore;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Infrastructure.Persistence;

namespace TicketService.Infrastructure.Implements.Services;

public sealed class OutboxClaimService : IOutboxClaimService
{
    private readonly TicketDbContext _dbContext;

    public OutboxClaimService(TicketDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<OutboxMessage?> TryClaimAsync(Guid messageId, string leaseOwner, TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var leaseUntil = now.Add(leaseDuration);
        var claimed = await _dbContext.OutboxMessages
            .Where(message => message.Id == messageId
                              && message.ProcessedAtUtc == null
                              && message.RetryCount >= 0
                              && (message.LeaseUntilUtc == null || message.LeaseUntilUtc <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.LeaseOwner, leaseOwner)
                .SetProperty(message => message.LeaseUntilUtc, leaseUntil), cancellationToken);

        if (claimed == 0)
        {
            return null;
        }

        return await _dbContext.OutboxMessages.AsNoTracking()
            .SingleAsync(message => message.Id == messageId && message.LeaseOwner == leaseOwner,
                cancellationToken);
    }

    public async Task<bool> MarkProcessedAsync(Guid messageId, string leaseOwner,
        CancellationToken cancellationToken = default)
    {
        var updated = await _dbContext.OutboxMessages
            .Where(message => message.Id == messageId
                              && message.ProcessedAtUtc == null
                              && message.LeaseOwner == leaseOwner)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.ProcessedAtUtc, DateTime.UtcNow)
                .SetProperty(message => message.LeaseOwner, (string?)null)
                .SetProperty(message => message.LeaseUntilUtc, (DateTime?)null), cancellationToken);

        return updated == 1;
    }

    public async Task<bool> MarkFailedAsync(Guid messageId, string leaseOwner, string error,
        CancellationToken cancellationToken = default)
    {
        var updated = await _dbContext.OutboxMessages
            .Where(message => message.Id == messageId
                              && message.ProcessedAtUtc == null
                              && message.LeaseOwner == leaseOwner)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.RetryCount, message => message.RetryCount + 1)
                .SetProperty(message => message.LastError, error)
                .SetProperty(message => message.LeaseOwner, (string?)null)
                .SetProperty(message => message.LeaseUntilUtc, (DateTime?)null), cancellationToken);

        return updated == 1;
    }
}
