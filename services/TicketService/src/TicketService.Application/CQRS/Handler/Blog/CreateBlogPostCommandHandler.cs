using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Blog;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Blog;

public class CreateBlogPostCommandHandler : IRequestHandler<CreateBlogPostCommand, CommonResponse<BlogPostActionDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public CreateBlogPostCommandHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<BlogPostActionDTO>> Handle(CreateBlogPostCommand request, CancellationToken ct)
    {
        var slugExists = await _uow.BlogPosts.AnyAsync(x => x.Slug == request.Slug && !x.IsDeleted);
        if (slugExists)
            return new CommonResponse<BlogPostActionDTO>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Slug đã tồn tại. Vui lòng chọn slug khác."
            };

        if (request.BlogTemplateId.HasValue)
        {
            var templateExists = await _uow.BlogTemplates.AnyAsync(x => x.Id == request.BlogTemplateId.Value && !x.IsDeleted);
            if (!templateExists)
                return new CommonResponse<BlogPostActionDTO>
                {
                    IsSuccess = false,
                    StatusCode = 404,
                    Message = "Template không tồn tại."
                };
        }

        var post = new BlogPost
        {
            Id = Guid.NewGuid(),
            Title = request.Title,
            Slug = request.Slug,
            Summary = request.Summary,
            ContentHtml = request.ContentHtml,
            Status = BlogPostStatusEnum.Draft,
            Origin = BlogPostOriginEnum.Manual,
            BlogTemplateId = request.BlogTemplateId,
            AuthorUserId = request.CurrentUserId,
            CurrentVersion = 1,
        };

        var version = new BlogPostVersion
        {
            Id = Guid.NewGuid(),
            BlogPostId = post.Id,
            VersionNumber = 1,
            Title = request.Title,
            Summary = request.Summary,
            ContentHtml = request.ContentHtml,
            ChangedByUserId = request.CurrentUserId,
            ChangeNote = "Tạo bài viết",
        };

        await _uow.BeginTransactionAsync();
        try
        {
            await _uow.BlogPosts.AddAsync(post);
            await _uow.BlogPostVersions.AddAsync(version);
            await _uow.CommitTransactionAsync();
        }
        catch (Exception ex)
        {
            await _uow.RollbackTransactionAsync();
            return new CommonResponse<BlogPostActionDTO> { IsSuccess = false, StatusCode = 500, Message = ex.Message };
        }

        return new CommonResponse<BlogPostActionDTO>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Tạo bài blog thành công.",
            Data = new BlogPostActionDTO
            {
                Id = post.Id.ToString(),
                Title = post.Title,
                Status = post.Status,
                CurrentVersion = post.CurrentVersion,
            }
        };
    }
}
