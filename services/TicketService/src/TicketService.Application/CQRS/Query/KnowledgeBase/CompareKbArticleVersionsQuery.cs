using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBase;

namespace TicketService.Application.CQRS.Query.KnowledgeBase;

public class CompareKbArticleVersionsQuery : IRequest<CommonResponse<KbArticleDiffDto>>
{
    public Guid ArticleId { get; set; }
    public Guid FromVersionId { get; set; }
    public Guid? ToVersionId { get; set; }
}
