using TicketService.Domain.Enums;

namespace TicketService.Application.Interfaces.Services;

public interface IChatAiSuggestionClient
{
    /// <summary>
    /// Gọi LLM (Gemini) để sinh <paramref name="count"/> gợi ý chat.
    /// <paramref name="context"/> đã được mask PII trước khi truyền vào (#559).
    /// </summary>
    Task<IReadOnlyList<string>> SuggestAsync(
        string context,
        ChatAiIntentEnum intent,
        string? category,
        int count,
        CancellationToken ct = default);
}
