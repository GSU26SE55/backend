using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBases;

namespace TicketService.Application.CQRS.Query.KnowledgeBase;

public class SuggestKbArticlesQuery : IRequest<CommonResponse<List<KbArticleSuggestDTO>>>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    public Guid TicketId { get; set; }
}
