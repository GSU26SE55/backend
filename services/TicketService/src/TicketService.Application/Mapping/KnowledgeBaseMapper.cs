using System.Text.Json;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Domain.Entities;

namespace TicketService.Application.Mapping;

public static class KnowledgeBaseMapper
{
    // JsonDocument → string: unwrap JSON string, else return raw text
    public static string J(JsonDocument? doc) =>
        doc is null
            ? string.Empty
            : doc.RootElement.ValueKind == JsonValueKind.String
                ? doc.RootElement.GetString() ?? string.Empty
                : doc.RootElement.GetRawText();

    // string → JsonDocument: try parse JSON, fall back to JSON-encoded string
    public static JsonDocument ToJsonDoc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return JsonDocument.Parse("{}");
        try
        { return JsonDocument.Parse(value); }
        catch { return JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(value)); }
    }

    public static KbArticleDTO ToDto(KnowledgeBaseArticle article)
    {
        return new KbArticleDTO
        {
            Id = article.Id.ToString(),
            Code = article.Code,
            Category = article.Category,
            Title = article.Title,
            Content = J(article.Content),
            Tags = article.Tags.ToList(),
            Status = article.Status,
            IsInternalOnly = article.IsInternalOnly,
            IsTemplate = article.IsTemplate,
            Version = article.Version,
            ViewCount = article.ViewCount,
            HelpfulCount = article.HelpfulCount,
            CreatedByUserId = article.CreatedByUserId.ToString(),
            PendingReviewBy = article.PendingReviewBy?.ToString(),
            ReviewRequired = article.ReviewRequired,
            ManagerRejectReason = article.ManagerRejectReason,
            CreatedAt = article.CreatedAt,
            UpdatedAt = article.UpdatedAt
        };
    }

    public static KbArticleListItemDTO ToListItemDto(KnowledgeBaseArticle a)
    {
        return new KbArticleListItemDTO
        {
            Id = a.Id.ToString(),
            Code = a.Code,
            Title = a.Title,
            Category = a.Category,
            Status = a.Status,
            IsTemplate = a.IsTemplate,
            ViewCount = a.ViewCount,
            HelpfulCount = a.HelpfulCount,
            ReviewRequired = a.ReviewRequired,
            CreatedAt = a.CreatedAt
        };
    }

    public static KbArticleSuggestDTO ToSuggestDto(KnowledgeBaseArticle a)
    {
        return new KbArticleSuggestDTO
        {
            Id = a.Id.ToString(),
            Code = a.Code,
            Title = a.Title,
            Content = J(a.Content),
            HelpfulCount = a.HelpfulCount,
            ViewCount = a.ViewCount,
            IsInternalOnly = a.IsInternalOnly
        };
    }

    public static KbArticleVersionDTO ToVersionDto(KbArticleVersion v)
    {
        return new KbArticleVersionDTO
        {
            Id = v.Id.ToString(),
            ArticleId = v.ArticleId.ToString(),
            MajorVersion = v.MajorVersion,
            MinorVersion = v.MinorVersion,
            Status = v.Status,
            Title = v.Title,
            Content = J(v.Content),
            Tags = v.Tags,
            ChangeDescription = v.ChangeDescription,
            ChangedBy = v.ChangedBy.ToString(),
            CreatedAt = v.CreatedAt
        };
    }

    public static KbArticleTemplateDTO ToTemplateDto(KnowledgeBaseArticle article)
    {
        return new KbArticleTemplateDTO
        {
            Category = article.Category,
            Content = J(article.Content),
            Tags = article.Tags.ToList()
        };
    }
}
