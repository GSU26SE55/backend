using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.StateMachine;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketReopenCommandHandler : IRequestHandler<TicketReopenCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;

    public TicketReopenCommandHandler(ITicketUnitOfWork uow, ITicketStateMachine stateMachine,
        IActivityLogger activityLogger, IIntegrationEventOutboxWriter outboxWriter, IPublisher publisher)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _activityLogger = activityLogger;
        _outboxWriter = outboxWriter;
    }

    public async Task<TicketActionResponse> Handle(TicketReopenCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(x => x.Id == request.TicketId && !x.IsDeleted, ct);
        if (ticket is null)
            return Fail(404, "Ticket not found.");
        if (ticket.CustomerId != request.CustomerId)
            return Fail(403, "Only the ticket owner can reopen this ticket.");
        if (ticket.Status != TicketStatusEnum.Closed || ticket.CloseReason == TicketCloseReasonEnum.MergedDuplicate)
            return Fail(409, "Only a non-merged Closed ticket can be reopened.");
        if (ticket.RatedAt.HasValue || ticket.Rating.HasValue)
            return Fail(409, "A rated ticket cannot be reopened.");
        if (!ticket.ClosedAt.HasValue || DateTime.UtcNow - ticket.ClosedAt.Value > TimeSpan.FromDays(7))
            return Fail(409, "The seven-day reopen window has expired.");

        // Giữ lại mốc đóng trước khi state machine chuyển Closed -> Open. BatteryService dùng
        // mốc này làm provenance để không mở lại một alert đã resolve ở chu kỳ ticket cũ.
        var previousClosedAt = ticket.ClosedAt.Value;

        var transition = _stateMachine.CanTransition(ticket, TicketStatusEnum.Open, ActorRoleEnum.Customer, request.CustomerId);
        if (!transition.IsAllowed)
            return Fail(403, transition.Reason ?? "The ticket cannot be reopened.");

        await _uow.ExecuteInTransactionAsync(async transactionCt =>
        {
            await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.Open, new TransitionContext
            {
                ActorUserId = request.CustomerId,
                ActorRole = ActorRoleEnum.Customer,
                ActorDisplayName = request.CustomerName ?? "Customer",
                Payload = new() { ["ReopenReason"] = request.ReopenReason.Trim() }
            }, transactionCt);

            var activeAssignments = await _uow.TicketAssignments.GetAllAsync()
                .Where(x => x.TicketId == ticket.Id && !x.IsDeleted && x.Role == AssignmentRoleEnum.PrimaryHandler)
                .ToListAsync(transactionCt);
            foreach (var assignment in activeAssignments)
            {
                assignment.Role = AssignmentRoleEnum.PreviousPrimaryHandler;
                _uow.TicketAssignments.UpdateAsync(assignment);
            }

            await _activityLogger.LogAsync(ticket.Id, request.CustomerId, ActorRoleEnum.Customer,
                request.CustomerName, ActivityActionEnum.Reopened, newValue: request.ReopenReason.Trim());
            await _outboxWriter.WriteAsync(new TicketReopenedIntegrationEvent(
                ticket.Id, ticket.Code, request.CustomerId, request.ReopenReason.Trim()), transactionCt);
            await _outboxWriter.WriteAsync(new TicketReopenedEvent(
                ticket.Id, ticket.Code, ticket.CustomerId, null,
                request.ReopenReason.Trim(), ticket.ReopenCount, DateTime.UtcNow,
                previousClosedAt), transactionCt);
            await _uow.SaveChangesAsync(transactionCt);
        }, ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Ticket reopened for Manager planning.",
            Data = new TicketActionDTO { Id = ticket.Id.ToString(), Code = ticket.Code, Status = ticket.Status }
        };
    }

    private static TicketActionResponse Fail(int code, string message) => new()
    { IsSuccess = false, StatusCode = code, Message = message };
}
