using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Query.KnowledgeBase;

public class GetKbArticleListQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<KbArticleListItemDTO>>>
{
    public TicketCategoryEnum? Category { get; set; }
    public KbArticleStatusEnum? Status { get; set; }
    public string? Tag { get; set; }
    public string? Q { get; set; }
}
