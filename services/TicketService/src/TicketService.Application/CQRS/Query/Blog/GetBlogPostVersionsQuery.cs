using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Blog;

namespace TicketService.Application.CQRS.Query.Blog;

public class GetBlogPostVersionsQuery : IRequest<CommonResponse<List<BlogPostVersionDTO>>>
{
    public Guid BlogPostId { get; set; }
}
