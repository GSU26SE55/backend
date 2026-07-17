using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Blog;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Blog;

public class GetBlogPostVersionsQueryHandler : IRequestHandler<GetBlogPostVersionsQuery, CommonResponse<List<BlogPostVersionDTO>>>
{
    private readonly ITicketUnitOfWork _uow;

    public GetBlogPostVersionsQueryHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<List<BlogPostVersionDTO>>> Handle(GetBlogPostVersionsQuery request, CancellationToken ct)
    {
        var postExists = await _uow.BlogPosts.AnyAsync(x => x.Id == request.BlogPostId && !x.IsDeleted);
        if (!postExists)
            return new CommonResponse<List<BlogPostVersionDTO>> { IsSuccess = false, StatusCode = 404, Message = "Bài viết không tìm thấy." };

        var versions = await _uow.BlogPostVersions.GetAllAsync()
            .Where(x => x.BlogPostId == request.BlogPostId && !x.IsDeleted)
            .OrderByDescending(x => x.VersionNumber)
            .Select(x => new BlogPostVersionDTO
            {
                Id = x.Id.ToString(),
                BlogPostId = x.BlogPostId.ToString(),
                VersionNumber = x.VersionNumber,
                Title = x.Title,
                Summary = x.Summary,
                ContentHtml = x.ContentHtml,
                ChangedByUserId = x.ChangedByUserId.ToString(),
                ChangeNote = x.ChangeNote,
                CreatedAt = x.CreatedAt,
            })
            .ToListAsync(ct);

        return new CommonResponse<List<BlogPostVersionDTO>> { IsSuccess = true, StatusCode = 200, Data = versions };
    }
}
