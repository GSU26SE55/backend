using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Domain.Enums;

namespace TicketService.Application.Interfaces.Services;

public interface IKbSuggestionService
{
    Task<IReadOnlyList<KbArticleSuggestDTO>> SuggestByChatBodyAsync(
        string chatBody,
        TicketCategoryEnum category,
        int topN,
        CancellationToken ct);
}
