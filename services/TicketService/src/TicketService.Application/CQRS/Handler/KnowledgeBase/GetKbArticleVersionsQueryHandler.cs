using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class GetKbArticleVersionsQueryHandler : IRequestHandler<GetKbArticleVersionsQuery, CommonResponse<List<KbArticleVersionDTO>>>
{
    private readonly ITicketUnitOfWork _uow;

    public GetKbArticleVersionsQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<List<KbArticleVersionDTO>>> Handle(GetKbArticleVersionsQuery query, CancellationToken ct)
    {
        var versions = await _uow.KbArticleVersions.GetAllAsync()
            .Where(v => v.ArticleId == query.ArticleId)
            .OrderByDescending(v => v.MajorVersion)
            .ThenByDescending(v => v.MinorVersion)
            .ToListAsync(ct);

        return new CommonResponse<List<KbArticleVersionDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = versions.Select(KnowledgeBaseMapper.ToVersionDto).ToList()
        };
    }
}
