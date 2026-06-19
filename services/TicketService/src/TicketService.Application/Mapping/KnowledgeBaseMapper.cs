using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Domain.Entities;

namespace TicketService.Application.Mapping;

public static class KnowledgeBaseMapper
{
    public static KbArticleDTO ToDto(KnowledgeBaseArticle article)
    {
        return new KbArticleDTO
        {
            Id = article.Id.ToString(),
            Code = article.Code,
            Category = article.Category,
            Title = article.Title,
            Symptoms = article.Symptoms,
            DiagnosisSteps = article.DiagnosisSteps,
            SolutionSteps = article.SolutionSteps,
            RecommendedParts = article.RecommendedParts,
            Tags = article.Tags.ToList(),
            Status = article.Status,
            IsInternalOnly = article.IsInternalOnly,
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
            Symptoms = a.Symptoms,
            HelpfulCount = a.HelpfulCount,
            ViewCount = a.ViewCount
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
            Status = (int)v.Status,
            Title = v.Title,
            Symptoms = v.Symptoms,
            DiagnosisSteps = v.DiagnosisSteps,
            SolutionSteps = v.SolutionSteps,
            RecommendedParts = v.RecommendedParts,
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
            Category = (int)article.Category,
            Symptoms = article.Symptoms,
            DiagnosisSteps = article.DiagnosisSteps,
            SolutionSteps = article.SolutionSteps,
            RecommendedParts = article.RecommendedParts,
            Tags = article.Tags.ToList()
        };
    }
}
