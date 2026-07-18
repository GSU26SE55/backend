using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Query.Ticket;

public class TicketGetListQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<TicketDTO>>>
{
    /// <summary>
    /// Từ khóa tìm kiếm.
    /// </summary>
    public string? Keyword { get; set; }
    public TicketStatusEnum? Status { get; set; }
    public TicketPriorityEnum? Priority { get; set; }
    /// <summary>
    /// Danh mục phân loại.
    /// </summary>
    public TicketCategoryEnum? Category { get; set; }
    public Guid? BatteryAssetId { get; set; }

    /// <summary>
    /// Đảo chiều theo CreatedAt (legacy — giữ tương thích ngược).
    /// Nếu <see cref="SortDir"/> được set thì SortDir thắng.
    /// </summary>
    public bool IsDescending { get; set; } = true;

    /// <summary>
    /// Cột sort. Whitelist: code | title | category | status | priority | createdAt.
    /// Giá trị ngoài whitelist → createdAt (mặc định).
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>Hướng sort: asc | desc. Nếu set sẽ ghi đè <see cref="IsDescending"/>.</summary>
    public string? SortDir { get; set; }
}
