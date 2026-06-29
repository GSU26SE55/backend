using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBases;

namespace TicketService.Application.CQRS.Query.KnowledgeBase;

public class CompareKbArticleVersionsQuery : IRequest<CommonResponse<KbArticleDiffDTO>>
{
    /// <summary>
    /// Article id.
    /// </summary>
    [BindNever]
    public Guid ArticleId { get; set; }
    public Guid FromVersionId { get; set; }
    /// <summary>
    /// To version id.
    /// </summary>
    public Guid? ToVersionId { get; set; }
}
