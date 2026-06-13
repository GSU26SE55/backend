using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBase;

namespace TicketService.Application.CQRS.Command.KnowledgeBase;

public class RejectReviewCommand : IRequest<CommonResponse<KbArticleDto>>
{
    public Guid ArticleId { get; set; }
    public string? Reason { get; set; }
}
