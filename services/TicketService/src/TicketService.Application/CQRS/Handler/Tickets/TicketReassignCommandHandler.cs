using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Ticket;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.StateMachine;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketReassignCommandHandler : IRequestHandler<TicketReassignCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;

    public TicketReassignCommandHandler(
        ITicketUnitOfWork uow,
        ITicketStateMachine stateMachine,
        IActivityLogger activityLogger)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _activityLogger = activityLogger;
    }

    public async Task<TicketActionResponse> Handle(TicketReassignCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (ticket == null)
            return Fail(404, "Ticket not found.");

        var transitionResult = _stateMachine.CanTransition(ticket, TicketStatusEnum.Assigned, ActorRoleEnum.Manager, request.ManagerId);
        if (!transitionResult.IsAllowed)
            return Fail(403, transitionResult.Reason ?? "Reassignment not allowed.");

        var oldStaffId = ticket.AssignedStaffId;
        ticket.AssignedStaffId = request.NewStaffId;

        await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.Assigned, new TransitionContext
        {
            ActorUserId = request.ManagerId,
            ActorRole = ActorRoleEnum.Manager,
            ActorDisplayName = request.ManagerName ?? "Manager"
        }, ct);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.ManagerId,
            ActorRoleEnum.Manager,
            request.ManagerName ?? "Manager",
            ActivityActionEnum.StaffReassigned,
            oldValue: oldStaffId?.ToString(),
            newValue: request.NewStaffId.ToString(),
            reason: request.Reason);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Ticket reassigned successfully.",
            Data = new TicketActionDto
            {
                Id = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
            }
        };
    }

    private static TicketActionResponse Fail(int statusCode, string message, string field = "Ticket")
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
