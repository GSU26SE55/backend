using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Blog;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Blog;

public class GetBlogPostByIdQueryHandler : IRequestHandler<GetBlogPostByIdQuery, CommonResponse<BlogPostDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public GetBlogPostByIdQueryHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<BlogPostDTO>> Handle(GetBlogPostByIdQuery request, CancellationToken ct)
    {
        var post = await _uow.BlogPosts.GetAllAsync()
            .Where(x => x.Id == request.BlogPostId && !x.IsDeleted)
            .Select(x => new BlogPostDTO
            {
                Id = x.Id.ToString(),
                Title = x.Title,
                Slug = x.Slug,
                Summary = x.Summary,
                ContentHtml = x.ContentHtml,
                Status = x.Status,
                Origin = x.Origin,
                SourceKbArticleId = x.SourceKbArticleId.HasValue ? x.SourceKbArticleId.Value.ToString() : null,
                BlogTemplateId = x.BlogTemplateId.HasValue ? x.BlogTemplateId.Value.ToString() : null,
                AuthorUserId = x.AuthorUserId.ToString(),
                CurrentVersion = x.CurrentVersion,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
            })
            .FirstOrDefaultAsync(ct);

        if (post == null)
            return new CommonResponse<BlogPostDTO> { IsSuccess = false, StatusCode = 404, Message = "Bài viết không tìm thấy." };

        return new CommonResponse<BlogPostDTO> { IsSuccess = true, StatusCode = 200, Data = post };
    }
}
