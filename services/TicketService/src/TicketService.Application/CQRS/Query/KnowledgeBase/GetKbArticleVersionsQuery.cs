using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBases;

namespace TicketService.Application.CQRS.Query.KnowledgeBase;

public class GetKbArticleVersionsQuery : IRequest<CommonResponse<List<KbArticleVersionDTO>>>
{
    [BindNever]
    public Guid ArticleId { get; set; }
}
