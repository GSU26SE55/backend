using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBase;

namespace TicketService.Application.CQRS.Query.KnowledgeBase;

public class SuggestKbArticlesQuery : IRequest<CommonResponse<List<KbArticleSuggestDto>>>
{
    public Guid TicketId { get; set; }
}
