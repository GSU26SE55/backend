using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Mapping;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class GetKbArticleListQueryHandler : IRequestHandler<GetKbArticleListQuery, CommonResponse<PaginationResponse<KbArticleListItemDTO>>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketCurrentUserService _currentUserService;

    public GetKbArticleListQueryHandler(ITicketUnitOfWork uow, ITicketCurrentUserService currentUserService)
    {
        _uow = uow;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<PaginationResponse<KbArticleListItemDTO>>> Handle(GetKbArticleListQuery query, CancellationToken ct)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var customerId))
        {
            return new CommonResponse<PaginationResponse<KbArticleListItemDTO>>
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Chưa đăng nhập."
            };
        }

        var dbQuery = _uow.KnowledgeBaseArticles.GetAllAsync()
            .Where(a => !a.IsDeleted);

        // Role-based filtering
        if (_currentUserService.Role.Equals("Customer", StringComparison.OrdinalIgnoreCase))
        {
            dbQuery = dbQuery.Where(a => a.Status == KbArticleStatusEnum.Published && !a.IsInternalOnly);
        }
        else
        {
            // Internal roles can filter by status
            if (query.Status.HasValue)
                dbQuery = dbQuery.Where(a => a.Status == query.Status.Value);
        }

        if (query.Category.HasValue)
            dbQuery = dbQuery.Where(a => a.Category == query.Category.Value);

        if (!string.IsNullOrWhiteSpace(query.Tag))
            dbQuery = dbQuery.Where(a => a.Tags.Contains(query.Tag));

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var search = query.Q.ToLower();
            dbQuery = dbQuery.Where(a => a.Title.ToLower().Contains(search) || a.Symptoms.ToLower().Contains(search));
        }

        var totalItems = await dbQuery.CountAsync(ct);

        var descending = SortHelper.IsDescending(query.SortDir);
        // Whitelist switch-case: code | title | category | status | viewCount | helpfulCount | createdAt (default).
        var ordered = (query.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "code" => descending ? dbQuery.OrderByDescending(a => a.Code) : dbQuery.OrderBy(a => a.Code),
            "title" => descending ? dbQuery.OrderByDescending(a => a.Title) : dbQuery.OrderBy(a => a.Title),
            "category" => descending ? dbQuery.OrderByDescending(a => a.Category) : dbQuery.OrderBy(a => a.Category),
            "status" => descending ? dbQuery.OrderByDescending(a => a.Status) : dbQuery.OrderBy(a => a.Status),
            "viewcount" => descending ? dbQuery.OrderByDescending(a => a.ViewCount) : dbQuery.OrderBy(a => a.ViewCount),
            "helpfulcount" => descending ? dbQuery.OrderByDescending(a => a.HelpfulCount) : dbQuery.OrderBy(a => a.HelpfulCount),
            _ => descending ? dbQuery.OrderByDescending(a => a.CreatedAt) : dbQuery.OrderBy(a => a.CreatedAt),
        };

        var items = await ordered
            .ThenBy(a => a.Id) // tie-breaker cố định — pagination ổn định
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new CommonResponse<PaginationResponse<KbArticleListItemDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<KbArticleListItemDTO>
            {
                Items = items.Select(KnowledgeBaseMapper.ToListItemDto).ToList(),
                TotalItems = totalItems,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            }
        };
    }
}
