using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBase;

namespace TicketService.Application.CQRS.Command.KnowledgeBase;

public class RollbackKbArticleCommand : IRequest<CommonResponse<KbArticleDto>>
{
    public Guid ArticleId { get; set; }
    public Guid ToVersionId { get; set; }
    public Guid CurrentUserId { get; set; }
}
