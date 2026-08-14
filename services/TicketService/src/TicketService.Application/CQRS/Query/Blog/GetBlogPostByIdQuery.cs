using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Blog;

namespace TicketService.Application.CQRS.Query.Blog;

public class GetBlogPostByIdQuery : IRequest<CommonResponse<BlogPostDTO>>
{
    public Guid BlogPostId { get; set; }

    /// <summary>
    /// Chỉ trả bài đã Published. Endpoint public để <c>true</c>;
    /// endpoint internal đặt <c>false</c> để đọc được Draft/Generating.
    /// </summary>
    public bool RequirePublished { get; set; } = true;
}
