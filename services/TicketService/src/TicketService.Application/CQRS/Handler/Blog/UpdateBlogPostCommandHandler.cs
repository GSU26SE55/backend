using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Blog;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Blog;

public class UpdateBlogPostCommandHandler : IRequestHandler<UpdateBlogPostCommand, CommonResponse<BlogPostActionDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public UpdateBlogPostCommandHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<BlogPostActionDTO>> Handle(UpdateBlogPostCommand request, CancellationToken ct)
    {
        var post = await _uow.BlogPosts.GetAllAsync()
            .Where(x => x.Id == request.BlogPostId && !x.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (post == null)
            return new CommonResponse<BlogPostActionDTO> { IsSuccess = false, StatusCode = 404, Message = "Bài viết không tìm thấy." };

        if (post.Status == BlogPostStatusEnum.Generating)
            return new CommonResponse<BlogPostActionDTO> { IsSuccess = false, StatusCode = 409, Message = "Bài viết đang được AI tạo, vui lòng thử lại sau." };

        if (post.Status == BlogPostStatusEnum.Archived)
            return new CommonResponse<BlogPostActionDTO> { IsSuccess = false, StatusCode = 409, Message = "Bài viết đã được archive, không thể chỉnh sửa." };

        if (post.CurrentVersion != request.CurrentVersion)
            return new CommonResponse<BlogPostActionDTO>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Bài viết đã được cập nhật bởi người khác. Vui lòng tải lại và thử lại."
            };

        if (post.Slug != request.Slug)
        {
            var slugExists = await _uow.BlogPosts.AnyAsync(x => x.Slug == request.Slug && x.Id != post.Id && !x.IsDeleted);
            if (slugExists)
                return new CommonResponse<BlogPostActionDTO> { IsSuccess = false, StatusCode = 409, Message = "Slug đã tồn tại." };
        }

        var newVersionNumber = post.CurrentVersion + 1;
        var contentDoc = KnowledgeBaseMapper.ToJsonDoc(request.ContentHtml);

        var version = new BlogPostVersion
        {
            Id = Guid.NewGuid(),
            BlogPostId = post.Id,
            VersionNumber = newVersionNumber,
            Title = request.Title,
            Summary = request.Summary,
            ContentHtml = contentDoc,
            ChangedByUserId = request.CurrentUserId,
            ChangeNote = request.ChangeNote,
        };

        post.Title = request.Title;
        post.Slug = request.Slug;
        post.Summary = request.Summary;
        post.ContentHtml = contentDoc;
        post.CurrentVersion = newVersionNumber;

        await _uow.BeginTransactionAsync();
        try
        {
            await _uow.BlogPostVersions.AddAsync(version);
            _uow.BlogPosts.UpdateAsync(post);
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
            StatusCode = 200,
            Message = "Cập nhật bài blog thành công.",
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
