using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class MarkHelpfulCommandHandler : IRequestHandler<MarkHelpfulCommand, CommonResponse<object>>
{
    private readonly ITicketUnitOfWork _uow;

    public MarkHelpfulCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<object>> Handle(MarkHelpfulCommand command, CancellationToken ct)
    {
        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.ArticleId, ct);

        if (article == null)
            return Fail(404, "Không tìm thấy bài viết.");

        article.HelpfulCount++;
        _uow.KnowledgeBaseArticles.UpdateAsync(article);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<object> { IsSuccess = true, StatusCode = 200, Message = "Cảm ơn bạn đã phản hồi." };
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
