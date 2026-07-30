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
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketReassignCommandHandler : IRequestHandler<TicketReassignCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;
    private readonly IMessageProducerService _producer;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-26

    public TicketReassignCommandHandler(
        ITicketUnitOfWork uow,
        ITicketStateMachine stateMachine,
        IActivityLogger activityLogger,
        IMessageProducerService producer,
        IPublisher publisher)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _activityLogger = activityLogger;
        _producer = producer;
        _publisher = publisher;
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

        // Auto-chuyển participant của Staff cũ sang PreviousAssignee + tạo PrimaryAssignee mới (#528)
        if (oldStaffId.HasValue)
        {
            var oldParticipant = await _uow.TicketParticipants.GetAllAsync()
                .FirstOrDefaultAsync(p => p.TicketId == ticket.Id && p.UserId == oldStaffId.Value
                    && p.ParticipantType == ParticipantTypeEnum.PrimaryAssignee && p.RemovedAt == null && !p.IsDeleted, ct);

            if (oldParticipant != null)
            {
                oldParticipant.ParticipantType = ParticipantTypeEnum.PreviousAssignee;
                oldParticipant.CanPost = false;
                oldParticipant.CanViewInternal = true;
                _uow.TicketParticipants.UpdateAsync(oldParticipant);
            }
        }

        // Nếu NewStaffId đã có active row (vd PreviousAssignee từ lượt reassign trước) → update thay vì insert trùng
        var newStaffParticipant = await _uow.TicketParticipants.GetAllAsync()
            .FirstOrDefaultAsync(p => p.TicketId == ticket.Id && p.UserId == request.NewStaffId
                && p.RemovedAt == null && !p.IsDeleted, ct);

        if (newStaffParticipant != null)
        {
            newStaffParticipant.ParticipantType = ParticipantTypeEnum.PrimaryAssignee;
            newStaffParticipant.CanPost = true;
            newStaffParticipant.CanViewInternal = true;
            _uow.TicketParticipants.UpdateAsync(newStaffParticipant);
        }
        else
        {
            await _uow.TicketParticipants.AddAsync(new TicketParticipant
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                Ticket = ticket,
                UserId = request.NewStaffId,
                UserRole = ActorRoleEnum.Staff,
                ParticipantType = ParticipantTypeEnum.PrimaryAssignee,
                CanPost = true,
                CanViewInternal = true,
                AddedByUserId = request.ManagerId,
                AddedAt = DateTime.UtcNow
            });
        }

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

        // Outbox: Staff Reassigned — Sprint 6.2 NOTI-05 (#676) kèm CustomerId.
        await _producer.PublishAsync(
            new TicketAssignedEvent(ticket.Id, ticket.Code, request.NewStaffId, ticket.Priority.ToString()!, ticket.CustomerId), ct);

        // #AUDIT-26
        await _publisher.Publish(TicketService.Application.CQRS.Notification.Audit.TicketAuditTrailNotification.For(
            TicketAuditActionEnum.AssignedToStaff, ticket.Id, targetDisplay: ticket.Code,
            metadata: new Dictionary<string, object?> { ["newStaffId"] = request.NewStaffId, ["reassign"] = true }), ct);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Ticket reassigned successfully.",
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
