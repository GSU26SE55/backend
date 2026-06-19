using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBases;

namespace TicketService.Application.CQRS.Query.KnowledgeBase;

public class CopyKbArticleTemplateQuery : IRequest<CommonResponse<KbArticleTemplateDTO>>
{
    [BindNever]
    public Guid ArticleId { get; set; }
}
