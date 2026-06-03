using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Ticket;

namespace TicketService.Application.CQRS.Query.Ticket;

public class TicketGetByIdQuery : IRequest<CommonResponse<TicketDetailDTO>>
{
    public Guid Id { get; set; }
    public Guid? ActorUserId { get; set; }
    public IReadOnlyCollection<string> ActorRoles { get; set; } = Array.Empty<string>();
}
