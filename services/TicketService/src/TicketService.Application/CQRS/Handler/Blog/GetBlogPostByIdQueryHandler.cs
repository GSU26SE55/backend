using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Blog;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Blog;

public class GetBlogPostByIdQueryHandler : IRequestHandler<GetBlogPostByIdQuery, CommonResponse<BlogPostDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public GetBlogPostByIdQueryHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<BlogPostDTO>> Handle(GetBlogPostByIdQuery request, CancellationToken ct)
    {
        var entity = await _uow.BlogPosts.GetAllAsync()
            .Where(x => x.Id == request.BlogPostId && !x.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (entity == null)
            return new CommonResponse<BlogPostDTO> { IsSuccess = false, StatusCode = 404, Message = "Bài viết không tìm thấy." };

        // Endpoint public không được lộ bài Draft/Generating/Archived
        if (request.RequirePublished && entity.Status != BlogPostStatusEnum.Published)
            return new CommonResponse<BlogPostDTO> { IsSuccess = false, StatusCode = 404, Message = "Bài viết không tìm thấy." };

        return new CommonResponse<BlogPostDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new BlogPostDTO
            {
                Id = entity.Id.ToString(),
                Title = entity.Title,
                Slug = entity.Slug,
                Summary = entity.Summary,
                ContentHtml = KnowledgeBaseMapper.J(entity.ContentHtml),
                Status = entity.Status,
                Origin = entity.Origin,
                SourceKbArticleId = entity.SourceKbArticleId?.ToString(),
                BlogTemplateId = entity.BlogTemplateId?.ToString(),
                AuthorUserId = entity.AuthorUserId.ToString(),
                CurrentVersion = entity.CurrentVersion,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
            }
        };
    }

}
