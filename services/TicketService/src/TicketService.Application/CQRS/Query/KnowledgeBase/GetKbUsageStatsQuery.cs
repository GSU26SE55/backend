using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBases;

namespace TicketService.Application.CQRS.Query.KnowledgeBase;

public class GetKbUsageStatsQuery : IRequest<CommonResponse<KbUsageStatsDTO>>
{
    public Guid KbArticleId { get; set; }
}
