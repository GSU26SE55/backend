using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class DeleteKbArticleCommandHandler : IRequestHandler<DeleteKbArticleCommand, CommonResponse<object>>
{
    private readonly ITicketUnitOfWork _uow;

    public DeleteKbArticleCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<object>> Handle(DeleteKbArticleCommand command, CancellationToken ct)
    {
        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.ArticleId, ct);

        if (article == null)
            return Fail(404, "Không tìm thấy bài viết.");

        article.IsDeleted = true;
        article.DeletedAt = DateTime.UtcNow;
        _uow.KnowledgeBaseArticles.UpdateAsync(article);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<object> { IsSuccess = true, StatusCode = 200, Message = "Bài viết đã được xóa." };
    }

    private static CommonResponse<object> Fail(int statusCode, string message)
    {
        return new CommonResponse<object>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
