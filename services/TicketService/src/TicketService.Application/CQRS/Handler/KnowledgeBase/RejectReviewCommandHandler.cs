using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBase;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class RejectReviewCommandHandler : IRequestHandler<RejectReviewCommand, CommonResponse<KbArticleActionDto>>
{
    private readonly ITicketUnitOfWork _uow;

    public RejectReviewCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<KbArticleActionDto>> Handle(RejectReviewCommand command, CancellationToken ct)
    {
        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.ArticleId, ct);

        if (article == null)
            return Fail(404, "Không tìm thấy bài viết.");

        if (article.Status != KbArticleStatusEnum.PendingReview)
            return Fail(409, "Bài viết không ở trạng thái Chờ phê duyệt.");

        // Find pending versions and reject them
        var nextMajor = article.Version + 1;
        var pendingVersions = await _uow.KbArticleVersions.GetAllAsync()
            .Where(v => v.ArticleId == article.Id && v.MajorVersion == nextMajor && v.Status == KbVersionStatusEnum.Pending)
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
        article.Status = article.Version > 0 ? KbArticleStatusEnum.Published : KbArticleStatusEnum.Draft;
        article.ReviewRequired = false;
        article.PendingReviewBy = null;
        article.ManagerRejectReason = command.Reason;

        _uow.KnowledgeBaseArticles.UpdateAsync(article);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<KbArticleActionDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Yêu cầu thay đổi đã bị từ chối.",
            Data = new KbArticleActionDto
            {
                Id = article.Id.ToString(),
                Code = article.Code,
                Status = article.Status
            }
        };
    }

    private static CommonResponse<KbArticleActionDto> Fail(int statusCode, string message)
    {
        return new CommonResponse<KbArticleActionDto>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
