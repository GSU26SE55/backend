using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Blog;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Blog;

public class PublishBlogPostCommandHandler : IRequestHandler<PublishBlogPostCommand, CommonResponse<BlogPostActionDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public PublishBlogPostCommandHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<BlogPostActionDTO>> Handle(PublishBlogPostCommand request, CancellationToken ct)
    {
        var post = await _uow.BlogPosts.GetAllAsync()
            .Where(x => x.Id == request.BlogPostId && !x.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (post == null)
            return new CommonResponse<BlogPostActionDTO> { IsSuccess = false, StatusCode = 404, Message = "Bài viết không tìm thấy." };

        if (post.Status is BlogPostStatusEnum.Generating or BlogPostStatusEnum.GenerationFailed)
            return new CommonResponse<BlogPostActionDTO> { IsSuccess = false, StatusCode = 409, Message = "Bài viết chưa sẵn sàng để publish." };

        if (post.Status == BlogPostStatusEnum.Published)
            return new CommonResponse<BlogPostActionDTO> { IsSuccess = false, StatusCode = 409, Message = "Bài viết đã được publish." };

        if (post.Status == BlogPostStatusEnum.Archived)
            return new CommonResponse<BlogPostActionDTO> { IsSuccess = false, StatusCode = 409, Message = "Bài viết đã được archive, không thể publish." };

        post.Status = BlogPostStatusEnum.Published;
        _uow.BlogPosts.UpdateAsync(post);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<BlogPostActionDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Publish bài blog thành công.",
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
