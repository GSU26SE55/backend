using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class DeleteKbArticleCommandHandler : IRequestHandler<DeleteKbArticleCommand, CommonResponse<KbArticleActionDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public DeleteKbArticleCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<KbArticleActionDTO>> Handle(DeleteKbArticleCommand command, CancellationToken ct)
    {
        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.ArticleId, ct);

        if (article == null)
            return Fail(404, "Không tìm thấy bài viết.");

        article.IsDeleted = true;
        article.DeletedAt = DateTime.UtcNow;
        _uow.KnowledgeBaseArticles.UpdateAsync(article);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<KbArticleActionDTO> { IsSuccess = true, StatusCode = 200, Message = "Bài viết đã được xóa." };
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
