using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Ticket;

namespace TicketService.Application.CQRS.Query.Ticket;

public class TicketCommentsQuery : IRequest<CommonResponse<PaginationResponse<TicketCommentDTO>>>
{
    public Guid TicketId { get; set; }
    public Guid ActorUserId { get; set; }
    public string[] ActorRoles { get; set; } = Array.Empty<string>();
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
