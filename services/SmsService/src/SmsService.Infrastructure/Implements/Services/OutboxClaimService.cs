using Microsoft.EntityFrameworkCore;
using SmsService.Application.Interfaces.Services;
using SmsService.Domain.Entities;
using SmsService.Infrastructure.Persistence;

namespace SmsService.Infrastructure.Implements.Services;

/// <inheritdoc cref="IOutboxClaimService"/>
public sealed class OutboxClaimService : IOutboxClaimService
{
    private readonly SmsDbContext _dbContext;

    public OutboxClaimService(SmsDbContext dbContext) => _dbContext = dbContext;

    public async Task<OutboxMessage?> TryClaimAsync(Guid messageId, string leaseOwner, TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var leaseUntil = now.Add(leaseDuration);

        // Điều kiện nằm NGAY TRONG câu UPDATE: chưa xử lý, và chưa ai giữ (hoặc quyền đã hết hạn).
        // Đọc trước rồi ghi sau sẽ để hai replica cùng thấy dòng trống và cùng publish.
        var claimed = await _dbContext.OutboxMessages
            .Where(message => message.Id == messageId
                              && message.ProcessedAt == null
                              && (message.LeaseUntilUtc == null || message.LeaseUntilUtc <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.LeaseOwner, leaseOwner)
                .SetProperty(message => message.LeaseUntilUtc, leaseUntil), cancellationToken);

        if (claimed == 0)
            return null;

        return await _dbContext.OutboxMessages.AsNoTracking()
            .SingleAsync(message => message.Id == messageId && message.LeaseOwner == leaseOwner,
                cancellationToken);
    }

    public async Task<bool> MarkProcessedAsync(Guid messageId, string leaseOwner,
        CancellationToken cancellationToken = default)
    {
        // Đối chiếu chủ sở hữu: một instance đã treo quá lâu và mất quyền không được ghi đè kết quả
        // của instance đang giữ — nếu không, dòng bị đánh dấu xong trong khi lần publish thật chưa
        // chắc đã xảy ra.
        var updated = await _dbContext.OutboxMessages
            .Where(message => message.Id == messageId
                              && message.ProcessedAt == null
                              && message.LeaseOwner == leaseOwner)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.ProcessedAt, DateTime.UtcNow)
                .SetProperty(message => message.LastError, (string?)null)
                .SetProperty(message => message.LeaseOwner, (string?)null)
                .SetProperty(message => message.LeaseUntilUtc, (DateTime?)null), cancellationToken);

        return updated == 1;
    }

    public async Task<bool> MarkFailedAsync(Guid messageId, string leaseOwner, string error,
        CancellationToken cancellationToken = default)
    {
        var updated = await _dbContext.OutboxMessages
            .Where(message => message.Id == messageId
                              && message.ProcessedAt == null
                              && message.LeaseOwner == leaseOwner)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.RetryCount, message => message.RetryCount + 1)
                .SetProperty(message => message.LastError, error)
                .SetProperty(message => message.LeaseOwner, (string?)null)
                .SetProperty(message => message.LeaseUntilUtc, (DateTime?)null), cancellationToken);

        return updated == 1;
    }
}
