using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Query.KnowledgeBase;

public class GetKbArticleListQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<KbArticleListItemDTO>>>
{
    /// <summary>
    /// Danh mục phân loại.
    /// </summary>
    public TicketCategoryEnum? Category { get; set; }
    public KbArticleStatusEnum? Status { get; set; }
    public string? Tag { get; set; }
    /// <summary>
    /// Từ khóa tìm kiếm.
    /// </summary>
    public string? Q { get; set; }

    /// <summary>
    /// Cột sort. Whitelist: code | title | category | status | viewCount | helpfulCount.
    /// Giá trị ngoài whitelist → createdAt (mặc định).
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>Hướng sort: asc | desc. Mặc định desc.</summary>
    public string? SortDir { get; set; }
}
