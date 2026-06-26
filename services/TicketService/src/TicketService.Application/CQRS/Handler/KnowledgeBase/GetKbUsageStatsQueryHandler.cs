using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class GetKbUsageStatsQueryHandler : IRequestHandler<GetKbUsageStatsQuery, CommonResponse<KbUsageStatsDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public GetKbUsageStatsQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<KbUsageStatsDTO>> Handle(GetKbUsageStatsQuery query, CancellationToken ct)
    {
        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == query.KbArticleId && !a.IsDeleted, ct);

        if (article == null)
            return new CommonResponse<KbUsageStatsDTO> { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy bài viết Knowledge Base." };

        var references = await _uow.TicketKbReferences.GetAllAsync()
            .Where(r => r.KbArticleId == query.KbArticleId && !r.IsDeleted)
            .ToListAsync(ct);

        var byType = new KbUsageByTypeDTO
        {
            ConsultedDuringResolve = references.Count(r => r.ReferenceType == KbReferenceTypeEnum.ConsultedDuringResolve),
            ProvidedToCustomer = references.Count(r => r.ReferenceType == KbReferenceTypeEnum.ProvidedToCustomer),
            GeneratedAfterResolve = references.Count(r => r.ReferenceType == KbReferenceTypeEnum.GeneratedAfterResolve)
        };

        return new CommonResponse<KbUsageStatsDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new KbUsageStatsDTO
            {
                KbArticleId = article.Id.ToString(),
                KbArticleCode = article.Code,
                KbArticleTitle = article.Title,
                TotalReferences = references.Count,
                ByType = byType
            }
        };
    }
}
