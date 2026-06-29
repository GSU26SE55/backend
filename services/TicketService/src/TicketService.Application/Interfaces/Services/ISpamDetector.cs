namespace TicketService.Application.Interfaces.Services;

/// <summary>
/// Phát hiện spam chat — cùng nội dung lặp ≥3 lần trong sliding window 5 phút,
/// theo cặp (ticketId, userId) (#518).
/// </summary>
public interface ISpamDetector
{
    /// <summary>
    /// Ghi nhận <paramref name="body"/> vừa được post và trả về <c>true</c> nếu đây là lần lặp
    /// thứ 3 (hoặc hơn) của cùng nội dung trong window — caller nên reject request khi <c>true</c>.
    /// </summary>
    Task<bool> IsSpamAsync(Guid ticketId, Guid userId, string body, CancellationToken cancellationToken = default);
}
