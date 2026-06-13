using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBase;

namespace TicketService.Application.CQRS.Query.KnowledgeBase;

public class GetKbArticleVersionByIdQuery : IRequest<CommonResponse<KbArticleVersionDto>>
{
    public Guid VersionId { get; set; }
}
