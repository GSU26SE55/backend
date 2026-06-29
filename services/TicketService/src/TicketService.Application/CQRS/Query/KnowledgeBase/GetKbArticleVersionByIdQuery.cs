using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBases;

namespace TicketService.Application.CQRS.Query.KnowledgeBase;

public class GetKbArticleVersionByIdQuery : IRequest<CommonResponse<KbArticleVersionDTO>>
{
    /// <summary>
    /// Version id.
    /// </summary>
    public Guid VersionId { get; set; }
}
