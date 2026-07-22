using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Blog;

namespace TicketService.Application.CQRS.Query.Blog;

public class CompareBlogPostVersionsQuery : IRequest<CommonResponse<BlogDiffDTO>>
{
    public Guid BlogPostId { get; set; }
    public int OldVersionNumber { get; set; }
    public int NewVersionNumber { get; set; }
}
