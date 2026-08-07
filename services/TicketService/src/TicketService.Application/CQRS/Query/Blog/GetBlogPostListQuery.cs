using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Query.Blog;

public class GetBlogPostListQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<BlogPostListItemDTO>>>
{
    public BlogPostStatusEnum? Status { get; set; }
    public BlogPostOriginEnum? Origin { get; set; }
    /// <summary>
    /// Từ khoá tìm theo Title / Summary. Bỏ trống = không lọc.
    /// Trước đây FE phải lọc client-side nên chỉ tìm được trong trang hiện tại.
    /// </summary>
    public string? Q { get; set; }
    /// <summary>Set by InternalBlogController — skip Published-only default filter.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsInternal { get; set; }
}
