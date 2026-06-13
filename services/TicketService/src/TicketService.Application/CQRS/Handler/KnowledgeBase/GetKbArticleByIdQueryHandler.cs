using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBase;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;
using TicketService.Domain.Entities;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class GetKbArticleByIdQueryHandler : IRequestHandler<GetKbArticleByIdQuery, CommonResponse<KbArticleDto>>
{
    private readonly ITicketUnitOfWork _uow;

    public GetKbArticleByIdQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<KbArticleDto>> Handle(GetKbArticleByIdQuery query, CancellationToken ct)
    {
        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == query.ArticleId, ct);

        if (article == null || article.IsDeleted)
            return Fail(404, "Không tìm thấy bài viết.");

        article.ViewCount++;
        _uow.KnowledgeBaseArticles.UpdateAsync(article);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<KbArticleDto>
        {
            IsSuccess = true,
            StatusCode = 200,
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
