using MediatR;
using SharedContracts.Common.Responses;

namespace TicketService.Application.CQRS.Command.KnowledgeBase;

public class MarkHelpfulCommand : IRequest<CommonResponse<object>>
{
    public Guid ArticleId { get; set; }
}
