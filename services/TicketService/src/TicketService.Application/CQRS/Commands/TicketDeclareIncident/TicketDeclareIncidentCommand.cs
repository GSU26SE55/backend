using MediatR;
using TicketService.Application.DTOs.Response.Ticket;

namespace TicketService.Application.CQRS.Commands.TicketDeclareIncident;

public record TicketDeclareIncidentCommand(Guid TicketId, Guid UserId) : IRequest<TicketActionResponse>;
