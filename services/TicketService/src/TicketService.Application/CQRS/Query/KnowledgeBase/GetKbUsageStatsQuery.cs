using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBases;

namespace TicketService.Application.CQRS.Query.KnowledgeBase;

public class GetKbUsageStatsQuery : IRequest<CommonResponse<KbUsageStatsDTO>>
{
    /// <summary>
    /// ID của bài viết Knowledge Base.
    /// </summary>
    public Guid KbArticleId { get; set; }
}
