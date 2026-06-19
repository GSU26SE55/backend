using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBases;

namespace TicketService.Application.CQRS.Query.KnowledgeBase;

public class GetKbArticleListQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<KbArticleListItemDTO>>>
{
    public int? Category { get; set; }
    public int? Status { get; set; }
    public string? Tag { get; set; }
    public string? Q { get; set; }
}
