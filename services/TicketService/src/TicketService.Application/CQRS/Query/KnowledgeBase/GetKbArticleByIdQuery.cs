using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBases;

namespace TicketService.Application.CQRS.Query.KnowledgeBase;

public class GetKbArticleByIdQuery : IRequest<CommonResponse<KbArticleDTO>>
{
    /// <summary>
    /// Article id.
    /// </summary>
    [BindNever]
    public Guid ArticleId { get; set; }

    /// <summary>
    /// Khi true, trả về 404 nếu article là template — dùng bởi Customer-facing endpoint.
    /// </summary>
    [BindNever]
    public bool RequireNonTemplate { get; set; } = false;
}
