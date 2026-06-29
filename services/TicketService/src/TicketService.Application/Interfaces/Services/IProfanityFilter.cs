namespace TicketService.Application.Interfaces.Services;

/// <summary>
/// Cảnh báo profanity VN/EN (config dict) — KHÔNG block post, chỉ gắn warning + log audit (#519).
/// </summary>
public interface IProfanityFilter
{
    bool ContainsProfanity(string body, out IReadOnlyList<string> matchedWords);
}
