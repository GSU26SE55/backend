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

public class ApproveReviewCommandHandler : IRequestHandler<ApproveReviewCommand, CommonResponse<KbArticleActionDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public ApproveReviewCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<KbArticleActionDTO>> Handle(ApproveReviewCommand command, CancellationToken ct)
    {
        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.ArticleId && !a.IsDeleted, ct);

        if (article == null)
            return Fail(404, "Article not found.");

        if (article.IsTemplate)
            return Fail(400, "Templates do not have an approval flow. Use the publish endpoint to publish templates.");

        if (article.Status != KbArticleStatusEnum.PendingReview)
            return Fail(409, "Article is not in Pending Review status.");

        // Find the latest pending version to approve
        var nextMajor = article.Version + 1;
        var pendingVersion = await _uow.KbArticleVersions.GetAllAsync()
            .Where(v => !v.IsDeleted && v.ArticleId == article.Id && v.MajorVersion == nextMajor && v.Status == KbVersionStatusEnum.Pending)
            .OrderByDescending(v => v.MinorVersion)
            .FirstOrDefaultAsync(ct);

        await _uow.BeginTransactionAsync();
        try
        {
            if (pendingVersion != null)
            {
                // Copy contents to main article
                article.Title = pendingVersion.Title;
                article.Content = pendingVersion.Content;
                article.Tags = pendingVersion.Tags.ToList();
                article.Version = nextMajor; // Update to new major version

                // Mark this version as approved
                pendingVersion.Status = KbVersionStatusEnum.Approved;
                _uow.KbArticleVersions.UpdateAsync(pendingVersion);

                // Optional: Mark other pending versions for this major version as rejected or obsolete
                var otherPendingVersions = await _uow.KbArticleVersions.GetAllAsync()
                    .Where(v => !v.IsDeleted && v.ArticleId == article.Id && v.MajorVersion == nextMajor && v.Status == KbVersionStatusEnum.Pending && v.Id != pendingVersion.Id)
                    .ToListAsync(ct);

                foreach (var v in otherPendingVersions)
                {
                    v.Status = KbVersionStatusEnum.Rejected;
                    v.ManagerRejectReason = "Another version has been approved.";
                    _uow.KbArticleVersions.UpdateAsync(v);
                }
            }

            article.Status = KbArticleStatusEnum.Draft;
            article.ReviewRequired = false;
            article.PendingReviewBy = null;
            article.ManagerRejectReason = null;

            _uow.KnowledgeBaseArticles.UpdateAsync(article);
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
            Message = "Change request has been approved and the content updated successfully.",
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
