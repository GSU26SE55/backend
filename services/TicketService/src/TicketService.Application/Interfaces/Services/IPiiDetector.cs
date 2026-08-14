namespace TicketService.Application.Interfaces.Services;

/// <summary>
/// Cảnh báo PII (CCCD/SĐT/email) qua regex — KHÔNG block post, chỉ gắn warning + log audit
/// nếu vẫn post (#519). Có thể false-positive (chấp nhận được vì chỉ là warning).
/// </summary>
public interface IPiiDetector
{
    bool ContainsPii(string body, out IReadOnlyList<string> matchedTypes);

    /// <summary>
    /// Thay thế PII bằng placeholder [CCCD-N]/[SĐT-N]/[EMAIL-N] và lưu mask map vào Redis TTL 1h
    /// để có thể un-mask sau (#559). Trả về (maskedText, maskKey) — maskKey là Redis key.
    /// </summary>
    Task<(string MaskedText, string MaskKey)> MaskAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Khôi phục PII từ placeholder bằng mask map đã lưu trong Redis.
    /// Nếu maskKey rỗng hoặc map đã hết TTL, trả về text gốc không thay đổi.
    /// </summary>
    Task<string> UnmaskAsync(string text, string maskKey, CancellationToken ct = default);
}
