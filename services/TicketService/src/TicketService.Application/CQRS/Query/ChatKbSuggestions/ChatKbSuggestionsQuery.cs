using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBases;

namespace TicketService.Application.CQRS.Query.ChatKbSuggestions;

public class ChatKbSuggestionsQuery : IRequest<CommonResponse<List<KbArticleSuggestDTO>>>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    public Guid TicketId { get; set; }
    public Guid ChatId { get; set; }
    public int TopN { get; set; } = 3;
}
