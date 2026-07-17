using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Query.Blog;

public class GetBlogPostListQuery : IRequest<CommonResponse<PaginationResponse<BlogPostListItemDTO>>>
{
    public BlogPostStatusEnum? Status { get; set; }
    public BlogPostOriginEnum? Origin { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    /// <summary>Set by InternalBlogController — skip Published-only default filter.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsInternal { get; set; }
}
