using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;
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

        // Tìm theo tiêu đề / tóm tắt. ToLower() 2 vế để không phân biệt hoa thường
        // trên Postgres (mặc định collation của Contains là case-sensitive).
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var q = request.Q.Trim().ToLower();
            query = query.Where(x => x.Title.ToLower().Contains(q) || x.Summary.ToLower().Contains(q));
        }

        // PaginationRequest đã clamp: PageNumber >= 1, PageSize trong [1, 100]
        var page = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id) // tie-breaker cố định — pagination ổn định
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
            .ToPagedEntityListAsync(request.PageNumber, request.PageSize, ct);

        return new CommonResponse<PaginationResponse<BlogPostListItemDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page
        };
    }
}
