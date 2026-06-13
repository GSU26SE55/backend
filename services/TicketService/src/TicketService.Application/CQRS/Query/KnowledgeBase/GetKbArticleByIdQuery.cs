using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBase;

namespace TicketService.Application.CQRS.Query.KnowledgeBase;

public class GetKbArticleByIdQuery : IRequest<CommonResponse<KbArticleDto>>
{
    public Guid ArticleId { get; set; }
}
