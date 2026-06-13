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

public class ArchiveKbArticleCommandHandler : IRequestHandler<ArchiveKbArticleCommand, CommonResponse<KbArticleDto>>
{
    private readonly ITicketUnitOfWork _uow;

    public ArchiveKbArticleCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<KbArticleDto>> Handle(ArchiveKbArticleCommand command, CancellationToken ct)
    {
        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.ArticleId, ct);

        if (article == null)
            return Fail(404, "Không tìm thấy bài viết.");

        article.Status = KbArticleStatusEnum.Archived;
        _uow.KnowledgeBaseArticles.UpdateAsync(article);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<KbArticleDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Bài viết đã được lưu trữ.",
            Data = KnowledgeBaseMapper.ToDto(article)
        };
    }

    private static CommonResponse<KbArticleDto> Fail(int statusCode, string message)
    {
        return new CommonResponse<KbArticleDto>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
