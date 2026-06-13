using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBase;
using TicketService.Domain.Entities;

namespace TicketService.Application.Mapping;

public static class KnowledgeBaseMapper
{
    public static KbArticleDto ToDto(KnowledgeBaseArticle article)
    {
        return new KbArticleDto
        {
            Id = article.Id.ToString(),
            Code = article.Code,
            Category = (int)article.Category,
            CategoryName = article.Category.ToString(),
            Title = article.Title,
            Symptoms = article.Symptoms,
            DiagnosisSteps = article.DiagnosisSteps,
            SolutionSteps = article.SolutionSteps,
            RecommendedParts = article.RecommendedParts,
            Tags = article.Tags,
            Status = (int)article.Status,
            StatusName = article.Status.ToString(),
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

    public static KbArticleListItemDto ToListItemDto(KnowledgeBaseArticle a)
    {
        return new KbArticleListItemDto
        {
            Id = a.Id.ToString(),
            Code = a.Code,
            Title = a.Title,
            Category = (int)a.Category,
            CategoryName = a.Category.ToString(),
            Status = (int)a.Status,
            StatusName = a.Status.ToString(),
            ViewCount = a.ViewCount,
            HelpfulCount = a.HelpfulCount,
            ReviewRequired = a.ReviewRequired,
            CreatedAt = a.CreatedAt
        };
    }

    public static KbArticleSuggestDto ToSuggestDto(KnowledgeBaseArticle a)
    {
        return new KbArticleSuggestDto
        {
            Id = a.Id.ToString(),
            Code = a.Code,
            Title = a.Title,
            Symptoms = a.Symptoms,
            HelpfulCount = a.HelpfulCount,
            ViewCount = a.ViewCount
        };
    }

    public static KbArticleVersionDto ToVersionDto(KbArticleVersion v)
    {
        return new KbArticleVersionDto
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

    public static KbArticleTemplateDto ToTemplateDto(KnowledgeBaseArticle article)
    {
        return new KbArticleTemplateDto
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
