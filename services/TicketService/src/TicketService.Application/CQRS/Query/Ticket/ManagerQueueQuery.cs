using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Query.Ticket;

public class ManagerQueueQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<TicketDTO>>>
{
    /// <summary>
    /// Mức độ ưu tiên.
    /// </summary>
    public TicketPriorityEnum? Priority { get; set; }
    public TicketCategoryEnum? Category { get; set; }
}
