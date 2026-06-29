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
    public bool IsDescending { get; set; } = true;
}
