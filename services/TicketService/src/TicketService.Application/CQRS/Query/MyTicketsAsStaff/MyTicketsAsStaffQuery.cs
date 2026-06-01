using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Query.MyTicketsAsStaff;

public class MyTicketsAsStaffQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<TicketDTO>>>
{
    public Guid ActorStaffId { get; set; }
    public TicketStatusEnum? Status { get; set; }
}
