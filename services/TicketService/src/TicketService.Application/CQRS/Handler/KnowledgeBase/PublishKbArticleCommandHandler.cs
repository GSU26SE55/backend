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
            .FirstOrDefaultAsync(a => a.Id == command.ArticleId, ct);

        if (article == null)
            return Fail(404, "Không tìm thấy bài viết.");

        if (article.Status != KbArticleStatusEnum.Draft && article.Status != KbArticleStatusEnum.PendingReview)
            return Fail(409, "Chỉ có thể xuất bản bài viết ở trạng thái Nháp hoặc Chờ phê duyệt.");

        article.Status = KbArticleStatusEnum.Published;
        article.ReviewRequired = false;
        article.PendingReviewBy = null;

        _uow.KnowledgeBaseArticles.UpdateAsync(article);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<KbArticleActionDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Bài viết đã được xuất bản thành công.",
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
