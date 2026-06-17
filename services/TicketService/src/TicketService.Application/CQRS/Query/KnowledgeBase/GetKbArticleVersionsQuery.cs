using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBase;

namespace TicketService.Application.CQRS.Query.KnowledgeBase;

public class GetKbArticleVersionsQuery : IRequest<CommonResponse<List<KbArticleVersionDto>>>
{
    public Guid ArticleId { get; set; }
}
