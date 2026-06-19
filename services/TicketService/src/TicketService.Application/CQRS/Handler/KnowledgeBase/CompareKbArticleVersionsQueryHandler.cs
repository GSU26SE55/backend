using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class CompareKbArticleVersionsQueryHandler : IRequestHandler<CompareKbArticleVersionsQuery, CommonResponse<KbArticleDiffDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public CompareKbArticleVersionsQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<KbArticleDiffDTO>> Handle(CompareKbArticleVersionsQuery query, CancellationToken ct)
    {
        var fromVersion = await _uow.KbArticleVersions.GetAllAsync()
            .FirstOrDefaultAsync(v => v.Id == query.FromVersionId, ct);

        if (fromVersion == null)
            return Fail(404, "Không tìm thấy phiên bản gốc.");

        KbArticleVersion? toVersion = null;
        KnowledgeBaseArticle? currentArticle = null;
        string toVersionLabel = string.Empty;

        if (query.ToVersionId == null) // Assume current article
        {
            currentArticle = await _uow.KnowledgeBaseArticles.GetAllAsync()
                .FirstOrDefaultAsync(a => a.Id == query.ArticleId, ct);

            if (currentArticle == null)
                return Fail(404, "Không tìm thấy bài viết hiện tại.");

            toVersionLabel = $"v{currentArticle.Version} (Current)";
        }
        else
        {
            toVersion = await _uow.KbArticleVersions.GetAllAsync()
                .FirstOrDefaultAsync(v => v.Id == query.ToVersionId, ct);

            if (toVersion == null)
                return Fail(404, "Không tìm thấy phiên bản đích.");

            toVersionLabel = $"v{toVersion.MajorVersion}.{toVersion.MinorVersion}";
        }

        var diff = new KbArticleDiffDTO
        {
            FromVersion = $"v{fromVersion.MajorVersion}.{fromVersion.MinorVersion}",
            ToVersion = toVersionLabel,
            TitleDiff = new DiffSection { OldValue = fromVersion.Title, NewValue = toVersion?.Title ?? currentArticle!.Title, IsChanged = fromVersion.Title != (toVersion?.Title ?? currentArticle!.Title) },
            SymptomsDiff = new DiffSection { OldValue = fromVersion.Symptoms, NewValue = toVersion?.Symptoms ?? currentArticle!.Symptoms, IsChanged = fromVersion.Symptoms != (toVersion?.Symptoms ?? currentArticle!.Symptoms) },
            DiagnosisStepsDiff = new DiffSection { OldValue = fromVersion.DiagnosisSteps, NewValue = toVersion?.DiagnosisSteps ?? currentArticle!.DiagnosisSteps, IsChanged = fromVersion.DiagnosisSteps != (toVersion?.DiagnosisSteps ?? currentArticle!.DiagnosisSteps) },
            SolutionStepsDiff = new DiffSection { OldValue = fromVersion.SolutionSteps, NewValue = toVersion?.SolutionSteps ?? currentArticle!.SolutionSteps, IsChanged = fromVersion.SolutionSteps != (toVersion?.SolutionSteps ?? currentArticle!.SolutionSteps) },
            RecommendedPartsDiff = new DiffSection
            {
                OldValue = fromVersion.RecommendedParts != null ? string.Join(", ", fromVersion.RecommendedParts) : "",
                NewValue = (toVersion?.RecommendedParts ?? currentArticle!.RecommendedParts) != null ? string.Join(", ", toVersion?.RecommendedParts ?? currentArticle!.RecommendedParts!) : "",
                IsChanged = !(fromVersion.RecommendedParts ?? new List<string>()).SequenceEqual(toVersion?.RecommendedParts ?? currentArticle!.RecommendedParts ?? new List<string>())
            },
            TagsDiff = new DiffSection { OldValue = string.Join(", ", fromVersion.Tags), NewValue = string.Join(", ", toVersion?.Tags ?? currentArticle!.Tags), IsChanged = !fromVersion.Tags.SequenceEqual(toVersion?.Tags ?? currentArticle!.Tags) }
        };

        return new CommonResponse<KbArticleDiffDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = diff
        };
    }

    private static CommonResponse<KbArticleDiffDTO> Fail(int statusCode, string message)
    {
        return new CommonResponse<KbArticleDiffDTO>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
