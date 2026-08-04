using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketDeclareIncidentCommandHandler : IRequestHandler<TicketDeclareIncidentCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _unitOfWork;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly IActivityLogger _activityLogger;

    public TicketDeclareIncidentCommandHandler(
        ITicketUnitOfWork unitOfWork,
        IIntegrationEventOutboxWriter producer,
        IActivityLogger activityLogger)
    {
        _unitOfWork = unitOfWork;
        _outboxWriter = producer;
        _activityLogger = activityLogger;
    }

    public async Task<TicketActionResponse> Handle(TicketDeclareIncidentCommand request, CancellationToken cancellationToken)
    {
        var ticket = await _unitOfWork.Tickets.GetByIdAsync(request.TicketId);
        if (ticket == null)
        {
            return Fail(404, "Ticket not found");
        }

        if (ticket.IsIncident)
        {
            return Fail(409, "Ticket is already an incident.");
        }

        ticket.IsIncident = true;
        // Optionally, we could also update the status to Incident if the state machine allows it
        // but for now, we follow the existing logic of just flipping the flag but with correct persistence.

        await _activityLogger.LogAsync(
            ticket.Id,
            request.UserId,
            ActorRoleEnum.Manager,
            request.UserDisplayName!,
            ActivityActionEnum.IncidentDeclared,
            reason: request.IncidentDescription);

        // Outbox: Incident Declared
        await _outboxWriter.WriteAsync(new IncidentDeclaredEvent(ticket.Id, ticket.Code, request.UserId), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Ticket declared as incident successfully.",
            Data = new TicketActionDTO
            {
                Id = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
            }
        };
    }

    private static TicketActionResponse Fail(int statusCode, string message)
    {
        return new TicketActionResponse
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message,
        };
    }
}
