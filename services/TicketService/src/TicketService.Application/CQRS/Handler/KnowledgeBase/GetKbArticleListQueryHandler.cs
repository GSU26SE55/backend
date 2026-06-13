using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBase;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class GetKbArticleListQueryHandler : IRequestHandler<GetKbArticleListQuery, CommonResponse<PaginationResponse<KbArticleListItemDto>>>
{
    private readonly ITicketUnitOfWork _uow;

    public GetKbArticleListQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<PaginationResponse<KbArticleListItemDto>>> Handle(GetKbArticleListQuery query, CancellationToken ct)
    {
        var dbQuery = _uow.KnowledgeBaseArticles.GetAllAsync()
            .Where(a => !a.IsDeleted);

        if (query.Category.HasValue)
            dbQuery = dbQuery.Where(a => (int)a.Category == query.Category.Value);

        if (query.Status.HasValue)
            dbQuery = dbQuery.Where(a => (int)a.Status == query.Status.Value);

        if (!string.IsNullOrWhiteSpace(query.Tag))
            dbQuery = dbQuery.Where(a => a.Tags.Contains(query.Tag));

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var search = query.Q.ToLower();
            dbQuery = dbQuery.Where(a => a.Title.ToLower().Contains(search) || a.Symptoms.ToLower().Contains(search));
        }

        var totalItems = await dbQuery.CountAsync(ct);
        var items = await dbQuery
            .OrderByDescending(a => a.CreatedAt)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new CommonResponse<PaginationResponse<KbArticleListItemDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<KbArticleListItemDto>
            {
                Items = items.Select(KnowledgeBaseMapper.ToListItemDto).ToList(),
                TotalItems = totalItems,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            }
        };
    }
}
