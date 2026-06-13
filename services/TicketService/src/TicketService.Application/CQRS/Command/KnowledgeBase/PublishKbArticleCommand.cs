using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBase;

namespace TicketService.Application.CQRS.Command.KnowledgeBase;

public class PublishKbArticleCommand : IRequest<CommonResponse<KbArticleDto>>
{
    public Guid ArticleId { get; set; }
}
