using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;
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
            return Fail(404, "Source version not found.");

        KbArticleVersion? toVersion = null;
        KnowledgeBaseArticle? currentArticle = null;
        string toVersionLabel = string.Empty;

        if (query.ToVersionId == null) // Assume current article
        {
            currentArticle = await _uow.KnowledgeBaseArticles.GetAllAsync()
                .FirstOrDefaultAsync(a => a.Id == query.ArticleId, ct);

            if (currentArticle == null)
                return Fail(404, "Current article not found.");

            toVersionLabel = $"v{currentArticle.Version} (Current)";
        }
        else
        {
            toVersion = await _uow.KbArticleVersions.GetAllAsync()
                .FirstOrDefaultAsync(v => v.Id == query.ToVersionId, ct);

            if (toVersion == null)
                return Fail(404, "Target version not found.");

            toVersionLabel = $"v{toVersion.MajorVersion}.{toVersion.MinorVersion}";
        }

        var fromContent = KnowledgeBaseMapper.J(fromVersion.Content);
        var toContent = KnowledgeBaseMapper.J(toVersion?.Content ?? currentArticle!.Content);
        var toTitle = toVersion?.Title ?? currentArticle!.Title;

        var diff = new KbArticleDiffDTO
        {
            FromVersion = $"v{fromVersion.MajorVersion}.{fromVersion.MinorVersion}",
            ToVersion = toVersionLabel,
            TitleDiff = new DiffSection { OldValue = fromVersion.Title, NewValue = toTitle, IsChanged = fromVersion.Title != toTitle },
            ContentDiff = new DiffSection { OldValue = fromContent, NewValue = toContent, IsChanged = fromContent != toContent },
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
