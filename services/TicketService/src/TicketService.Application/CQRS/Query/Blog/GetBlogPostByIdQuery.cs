using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Blog;

namespace TicketService.Application.CQRS.Query.Blog;

public class GetBlogPostByIdQuery : IRequest<CommonResponse<BlogPostDTO>>
{
    public Guid BlogPostId { get; set; }
}
