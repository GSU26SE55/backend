using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Entities;
using AuthService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuthService.UnitTests.Infrastructure.Outbox;

/// <summary>
/// GH-794 — bản <see cref="IOutboxClaimService"/> chạy được trên EF InMemory.
/// </summary>
/// <remarks>
/// <para>
/// Bản production dùng <c>ExecuteUpdateAsync</c> — đó chính là chỗ tạo ra tính nguyên tử, và
/// provider InMemory không hỗ trợ nó. Lớp này giữ nguyên <b>ngữ nghĩa</b> (chỉ giành được dòng chưa
/// xử lý và chưa ai giữ; chỉ chủ sở hữu mới đánh dấu xong/hỏng được) để các test đơn luồng về thứ
/// tự, kích thước lô và đếm lần thử vẫn chạy được.
/// </para>
/// <para>
/// Tính nguyên tử THẬT không kiểm được ở đây và cũng không giả vờ kiểm: nó được chứng minh trên
/// Postgres thật ở <c>AuthService.IntegrationTests/Outbox/OutboxClaimConcurrencyTests</c>.
/// </para>
/// </remarks>
public sealed class InMemoryOutboxClaimService : IOutboxClaimService
{
    private readonly ApplicationDbContext _db;

    public InMemoryOutboxClaimService(ApplicationDbContext db) => _db = db;

    public async Task<OutboxMessage?> TryClaimAsync(Guid messageId, string leaseOwner, TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var row = await _db.OutboxMessages.FirstOrDefaultAsync(o => o.Id == messageId, cancellationToken);

        if (row is null || row.ProcessedAt is not null)
            return null;
        if (row.LeaseUntilUtc is not null && row.LeaseUntilUtc > now)
            return null;

        row.LeaseOwner = leaseOwner;
        row.LeaseUntilUtc = now.Add(leaseDuration);
        await _db.SaveChangesAsync(cancellationToken);
        return row;
    }

    public async Task<bool> MarkProcessedAsync(Guid messageId, string leaseOwner,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.OutboxMessages.FirstOrDefaultAsync(o => o.Id == messageId, cancellationToken);
        if (row is null || row.ProcessedAt is not null || row.LeaseOwner != leaseOwner)
            return false;

        row.ProcessedAt = DateTime.UtcNow;
        row.LastError = null;
        row.LeaseOwner = null;
        row.LeaseUntilUtc = null;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> MarkFailedAsync(Guid messageId, string leaseOwner, string error,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.OutboxMessages.FirstOrDefaultAsync(o => o.Id == messageId, cancellationToken);
        if (row is null || row.ProcessedAt is not null || row.LeaseOwner != leaseOwner)
            return false;

        row.RetryCount += 1;
        row.LastError = error;
        row.LeaseOwner = null;
        row.LeaseUntilUtc = null;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
