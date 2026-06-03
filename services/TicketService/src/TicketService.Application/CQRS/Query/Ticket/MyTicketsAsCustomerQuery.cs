using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Ticket;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Query.Ticket;

public class MyTicketsAsCustomerQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<TicketDTO>>>
{
    public Guid ActorCustomerId { get; set; }
    public TicketStatusEnum? Status { get; set; }
}
