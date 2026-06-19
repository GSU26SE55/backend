using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBases;

namespace TicketService.Application.CQRS.Query.KnowledgeBase;

public class CompareKbArticleVersionsQuery : IRequest<CommonResponse<KbArticleDiffDTO>>
{
    [BindNever]
    public Guid ArticleId { get; set; }
    public Guid FromVersionId { get; set; }
    public Guid? ToVersionId { get; set; }
}
