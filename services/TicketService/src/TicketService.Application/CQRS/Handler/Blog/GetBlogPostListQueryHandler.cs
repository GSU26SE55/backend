using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Blog;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Blog;

public class GetBlogPostListQueryHandler : IRequestHandler<GetBlogPostListQuery, CommonResponse<PaginationResponse<BlogPostListItemDTO>>>
{
    private readonly ITicketUnitOfWork _uow;

    public GetBlogPostListQueryHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<PaginationResponse<BlogPostListItemDTO>>> Handle(GetBlogPostListQuery request, CancellationToken ct)
    {
        var query = _uow.BlogPosts.GetAllAsync().Where(x => !x.IsDeleted);

        if (request.Status.HasValue)
            query = query.Where(x => x.Status == request.Status.Value);
        else if (!request.IsInternal)
            query = query.Where(x => x.Status == BlogPostStatusEnum.Published);

        if (request.Origin.HasValue)
            query = query.Where(x => x.Origin == request.Origin.Value);

        var totalItems = await query.CountAsync(ct);

        // PaginationRequest đã clamp: PageNumber >= 1, PageSize trong [1, 100]
        var page = request.PageNumber;
        var pageSize = request.PageSize;

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new BlogPostListItemDTO
            {
                Id = x.Id.ToString(),
                Title = x.Title,
                Slug = x.Slug,
                Summary = x.Summary,
                Status = x.Status,
                Origin = x.Origin,
                AuthorUserId = x.AuthorUserId.ToString(),
                CurrentVersion = x.CurrentVersion,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
            })
            .ToListAsync(ct);

        return new CommonResponse<PaginationResponse<BlogPostListItemDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<BlogPostListItemDTO>
            {
                Items = items,
                TotalItems = totalItems,
                PageNumber = page,
                PageSize = pageSize,
            }
        };
    }
}
