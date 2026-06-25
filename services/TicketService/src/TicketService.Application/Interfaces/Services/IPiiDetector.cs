namespace TicketService.Application.Interfaces.Services;

/// <summary>
/// Cảnh báo PII (CCCD/SĐT/email) qua regex — KHÔNG block post, chỉ gắn warning + log audit
/// nếu vẫn post (#519). Có thể false-positive (chấp nhận được vì chỉ là warning).
/// </summary>
public interface IPiiDetector
{
    bool ContainsPii(string body, out IReadOnlyList<string> matchedTypes);
}
