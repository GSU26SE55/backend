namespace TicketService.Application.Interfaces.Services;

/// <summary>
/// Phát hiện spam chat — cùng nội dung lặp ≥3 lần trong sliding window 5 phút,
/// theo cặp (ticketId, userId) (#518).
/// </summary>
public interface ISpamDetector
{
    Task<SpamLease?> TryAcquireLeaseAsync(Guid ticketId, Guid userId, CancellationToken cancellationToken = default);
    Task<bool> RenewLeaseAsync(SpamLease lease, CancellationToken cancellationToken = default);
    Task ReleaseLeaseAsync(SpamLease lease, CancellationToken cancellationToken = default);
    /// <summary>
    /// Kiểm tra liệu <paramref name="body"/> có là lần lặp thứ ba (hoặc hơn) trong cửa sổ hay không.
    /// Chỉ gọi <see cref="RecordAcceptedMessageAsync"/> sau khi chat đã lưu thành công.
    /// </summary>
    Task<bool> IsSpamAsync(Guid ticketId, Guid userId, string body, CancellationToken cancellationToken = default);

    Task RecordAcceptedMessageAsync(Guid ticketId, Guid userId, string body, CancellationToken cancellationToken = default);
}

public record SpamLease(string Key, string OwnerToken);
