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

public class ArchiveKbArticleCommandHandler : IRequestHandler<ArchiveKbArticleCommand, CommonResponse<KbArticleActionDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public ArchiveKbArticleCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<KbArticleActionDTO>> Handle(ArchiveKbArticleCommand command, CancellationToken ct)
    {
        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.ArticleId, ct);

        if (article == null)
            return Fail(404, "Không tìm thấy bài viết.");

        article.Status = KbArticleStatusEnum.Archived;
        _uow.KnowledgeBaseArticles.UpdateAsync(article);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<KbArticleActionDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Bài viết đã được lưu trữ.",
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
