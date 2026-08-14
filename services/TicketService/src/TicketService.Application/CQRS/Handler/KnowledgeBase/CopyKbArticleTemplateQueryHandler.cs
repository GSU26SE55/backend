using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;
// using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class CopyKbArticleTemplateQueryHandler : IRequestHandler<CopyKbArticleTemplateQuery, CommonResponse<KbArticleTemplateDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public CopyKbArticleTemplateQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<KbArticleTemplateDTO>> Handle(CopyKbArticleTemplateQuery query, CancellationToken ct)
    {
        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == query.ArticleId, ct);

        if (article == null || article.IsDeleted)
            return Fail(404, "Article not found.");

        // if (!article.IsTemplate)
        //     return Fail(400, "Bài viết này không phải là bản mẫu.");
        // if (article.Status != KbArticleStatusEnum.Published)
        //     return Fail(400, "Chỉ có thể sao chép template đang ở trạng thái Xuất bản.");

        return new CommonResponse<KbArticleTemplateDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = KnowledgeBaseMapper.ToTemplateDto(article)
        };
    }

    private static CommonResponse<KbArticleTemplateDTO> Fail(int statusCode, string message)
    {
        return new CommonResponse<KbArticleTemplateDTO>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
