using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class PublishKbArticleCommandHandler : IRequestHandler<PublishKbArticleCommand, CommonResponse<KbArticleActionDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public PublishKbArticleCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<KbArticleActionDTO>> Handle(PublishKbArticleCommand command, CancellationToken ct)
    {
        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.ArticleId && !a.IsDeleted, ct);

        if (article == null)
            return Fail(404, "Article not found.");

        if (article.IsTemplate)
        {
            if (!command.CurrentUserRole.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                return Fail(403, "Only Admin can publish templates.");

            return await HandleTemplatePublish(article, ct);
        }

        if (article.Status != KbArticleStatusEnum.Draft)
            return Fail(409, "Only articles in Draft status can be published.");

        article.Status = KbArticleStatusEnum.Published;
        article.ReviewRequired = false;
        article.PendingReviewBy = null;

        _uow.KnowledgeBaseArticles.UpdateAsync(article);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<KbArticleActionDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Article published successfully.",
            Data = new KbArticleActionDTO
            {
                Id = article.Id.ToString(),
                Code = article.Code,
                Status = article.Status
            }
        };
    }

    private async Task<CommonResponse<KbArticleActionDTO>> HandleTemplatePublish(
        KnowledgeBaseArticle article, CancellationToken ct)
    {
        if (article.Status != KbArticleStatusEnum.Draft)
            return Fail(409, "Only templates in Draft status can be published.");

        await _uow.BeginTransactionAsync();
        try
        {
            article.Status = KbArticleStatusEnum.Published;
            _uow.KnowledgeBaseArticles.UpdateAsync(article);

            // Chuyển pending version sang Approved khi publish
            var pendingVersion = await _uow.KbArticleVersions.GetAllAsync()
                .FirstOrDefaultAsync(v => v.ArticleId == article.Id
                    && v.MajorVersion == article.Version + 1
                    && v.Status == KbVersionStatusEnum.Pending
                    && !v.IsDeleted, ct);

            if (pendingVersion != null)
            {
                pendingVersion.Status = KbVersionStatusEnum.Approved;
                article.Version = pendingVersion.MajorVersion;
                _uow.KbArticleVersions.UpdateAsync(pendingVersion);
            }

            await _uow.CommitTransactionAsync();
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }

        return new CommonResponse<KbArticleActionDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Template published successfully.",
            Data = new KbArticleActionDTO
            {
                Id = article.Id.ToString(),
                Code = article.Code,
                Status = article.Status
            }
        };
    }

    private static CommonResponse<KbArticleActionDTO> Fail(int statusCode, string message)
    {
        return new CommonResponse<KbArticleActionDTO>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
