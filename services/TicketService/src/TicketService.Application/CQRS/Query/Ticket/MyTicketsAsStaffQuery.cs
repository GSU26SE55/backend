using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Query.Ticket;

public class MyTicketsAsStaffQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<TicketDTO>>>
{
    /// <summary>
    /// Trạng thái.
    /// </summary>
    public TicketStatusEnum? Status { get; set; }

    /// <summary>
    /// true = chỉ lấy ticket đang trong vòng theo dõi SLA
    /// (status ∈ Assigned/InProgress/WaitingCustomer/WaitingParts/WaitingOnsiteSchedule/Escalated và có SLA timer)
    /// — phục vụ bảng SLA Monitor không bị cap theo pageSize.
    /// </summary>
    public bool? SlaOpen { get; set; }

    /// <summary>
    /// "slaRemaining" = sort theo hạn SLA (DueAt) tăng dần — ticket gần breach lên đầu.
    /// Mặc định (null/khác): Priority tăng dần rồi CreatedAt giảm dần.
    /// </summary>
    public string? SortBy { get; set; }
}
