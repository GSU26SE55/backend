using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.TicketDeclareIncident;
using TicketService.Application.DTOs.Response.Ticket;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketDeclareIncidentCommandHandler : IRequestHandler<TicketDeclareIncidentCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _producer;

    public TicketDeclareIncidentCommandHandler(ITicketUnitOfWork unitOfWork, IMessageProducerService producer)
    {
        _unitOfWork = unitOfWork;
        _producer = producer;
    }

    public async Task<TicketActionResponse> Handle(TicketDeclareIncidentCommand request, CancellationToken cancellationToken)
    {
        var ticket = await _unitOfWork.Tickets.GetByIdAsync(request.TicketId);
        if (ticket == null)
        {
            return Fail(404, "Ticket not found");
        }

        ticket.IsIncident = true;

        var activity = new TicketActivity
        {
            TicketId = ticket.Id,
            Action = ActivityActionEnum.IncidentDeclared,
            ActorUserId = request.UserId,
            ActorRole = ActorRoleEnum.Manager, // Assuming the user is a manager
            ActorDisplayName = "Manager", // This should be retrieved from the user claims
            Reason = "Ticket has been declared as an incident.",
            Ticket = ticket
        };

        await _unitOfWork.TicketActivities.AddAsync(activity);

        // Outbox: Incident Declared
        await _producer.PublishAsync(new IncidentDeclaredIntegrationEvent(ticket.Id, ticket.Code, request.UserId), cancellationToken);

        await _unitOfWork.CommitTransactionAsync();

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Ticket declared as incident successfully.",
            Data = new TicketActionDto
            {
                Id = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
            }
        };
    }

    private static TicketActionResponse Fail(int statusCode, string message, string field = "TicketId")
    {
        return new TicketActionResponse
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message,
            ListErrors = new List<Errors>
            {
                new Errors { Field = field, Detail = message }
            }
        };
    }
}
