using Microsoft.EntityFrameworkCore;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Mapping;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Implements.Services;

public class KbSuggestionService : IKbSuggestionService
{
    private readonly ITicketUnitOfWork _uow;

    public KbSuggestionService(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<IReadOnlyList<KbArticleSuggestDTO>> SuggestByChatBodyAsync(
        string chatBody,
        TicketCategoryEnum category,
        int topN,
        CancellationToken ct)
    {
        // Extract meaningful keywords (length ≥ 3, skip stopwords)
        var keywords = ExtractKeywords(chatBody);

        var baseQuery = _uow.KnowledgeBaseArticles.GetAllAsync()
            .Where(a => !a.IsDeleted
                && a.Status == KbArticleStatusEnum.Published
                && !a.IsInternalOnly
                && a.Category == category);

        if (keywords.Count > 0)
        {
            // Build explicit OR predicate per keyword — avoids EF Core translation issues
            // with List<string>.Any() inside IQueryable.Where().
            var ids = await baseQuery
                .Select(a => new { a.Id, a.Title, a.Content })
                .ToListAsync(ct);

            var matchedIds = ids
                .Where(a => keywords.Any(kw =>
                    a.Title.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                    KnowledgeBaseMapper.J(a.Content).Contains(kw, StringComparison.OrdinalIgnoreCase)))
                .Select(a => a.Id)
                .ToHashSet();

            baseQuery = baseQuery.Where(a => matchedIds.Contains(a.Id));
        }

        var suggestions = await baseQuery
            .OrderByDescending(a => a.HelpfulCount)
            .ThenByDescending(a => a.ViewCount)
            .Take(topN)
            .ToListAsync(ct);

        // Fallback: if no keyword match, return top by HelpfulCount for same category
        if (suggestions.Count == 0 && keywords.Count > 0)
        {
            suggestions = await _uow.KnowledgeBaseArticles.GetAllAsync()
                .Where(a => !a.IsDeleted && a.Status == KbArticleStatusEnum.Published && !a.IsInternalOnly && a.Category == category)
                .OrderByDescending(a => a.HelpfulCount)
                .Take(topN)
                .ToListAsync(ct);
        }

        return suggestions.Select(KnowledgeBaseMapper.ToSuggestDto).ToList();
    }

    private static List<string> ExtractKeywords(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new List<string>();

        var stopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "và", "hoặc", "nhưng", "mà", "vì", "để", "với", "của", "trong", "ngoài",
            "the", "a", "an", "is", "are", "was", "were", "in", "on", "at", "to", "for",
            "không", "có", "được", "bị", "đã", "sẽ", "đang", "this", "that", "it"
        };

        return body
            .Split(new[] { ' ', '\n', '\r', ',', '.', '!', '?', ';', ':', '(', ')', '[', ']' },
                StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && !stopwords.Contains(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }
}
