using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events.KnowledgeBase;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class RejectReviewCommandHandler : IRequestHandler<RejectReviewCommand, CommonResponse<KbArticleActionDTO>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;

    public RejectReviewCommandHandler(ITicketUnitOfWork uow, IIntegrationEventOutboxWriter outboxWriter)
    {
        _uow = uow;
        _outboxWriter = outboxWriter;
    }

    public async Task<CommonResponse<KbArticleActionDTO>> Handle(RejectReviewCommand command, CancellationToken ct)
    {
        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.ArticleId && !a.IsDeleted, ct);

        if (article == null)
            return Fail(404, "Article not found.");

        if (article.IsTemplate)
            return Fail(400, "Templates do not have an approval flow. Use the publish endpoint to publish templates.");

        if (article.Status != KbArticleStatusEnum.PendingReview)
            return Fail(409, "Article is not in Pending Review status.");

        // Find pending versions and reject them
        var nextMajor = article.Version + 1;
        var pendingVersions = await _uow.KbArticleVersions.GetAllAsync()
            .Where(v => !v.IsDeleted && v.ArticleId == article.Id && v.MajorVersion == nextMajor && v.Status == KbVersionStatusEnum.Pending)
            .ToListAsync(ct);

        foreach (var v in pendingVersions)
        {
            v.Status = KbVersionStatusEnum.Rejected;
            v.ManagerRejectReason = command.Reason;
            _uow.KbArticleVersions.UpdateAsync(v);
        }

        // Return article status to its previous stable state or Draft
        // Actually, if it has a Version > 0 it was probably Published, but the logic says return to Draft.
        // We'll just reset ReviewRequired and clear the PendingReviewBy.
        // For simplicity, we can keep the previous status if we tracked it, but let's just use Published if Version > 0, else Draft.
        // Người nhận thông báo PHẢI được đọc TRƯỚC khi PendingReviewBy bị xoá ngay dưới đây —
        // đọc sau thì luôn là null và thông báo không biết gửi cho ai.
        var submittedBy = article.PendingReviewBy;
        var articleTitle = article.Title;

        article.Status = article.Version > 0 ? KbArticleStatusEnum.Published : KbArticleStatusEnum.Draft;
        article.ReviewRequired = false;
        article.PendingReviewBy = null;
        article.ManagerRejectReason = command.Reason;

        _uow.KnowledgeBaseArticles.UpdateAsync(article);

        // Báo cho người đề xuất là bản sửa bị trả về, kèm lý do. Không tự gửi cho chính người
        // bấm từ chối (Manager tự từ chối bài mình gửi thì không cần báo lại cho mình).
        if (submittedBy.HasValue && submittedBy.Value != Guid.Empty && submittedBy.Value != command.CurrentUserId)
        {
            await _outboxWriter.WriteAsync(new KbArticleReviewDecidedEvent(
                article.Id,
                articleTitle,
                submittedBy.Value,
                command.CurrentUserId,
                command.CurrentUserName,
                Approved: false,
                RejectReason: command.Reason), ct);
        }

        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<KbArticleActionDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Change request has been rejected.",
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
