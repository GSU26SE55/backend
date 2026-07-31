using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Helpers;
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
    private readonly IPublisher _publisher;

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

        // Validate new PrimaryHandler staff
        var newStaff = await _uow.StaffAccounts.GetAllAsync()
            .Where(s => s.AccountId == request.NewPrimaryHandlerStaffId && !s.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (newStaff == null)
            return Fail(404, "Không tìm thấy thông tin nhân viên trong hệ thống.");

        if (newStaff.Status != AccountStatusEnum.Active)
            return Fail(403, "Tài khoản nhân viên PrimaryHandler đang bị khóa hoặc vô hiệu hóa.");

        if (!newStaff.IsAvailable)
            return Fail(403, "Nhân viên PrimaryHandler hiện đang không sẵn sàng nhận ticket mới.");

        if (ticket.Priority.HasValue && !AssignmentRoleHelper.ValidatePrimaryHandlerTier(ticket.Priority.Value, newStaff.SkillTier))
        {
            var required = AssignmentRoleHelper.GetTierRequirementMessage(ticket.Priority.Value);
            return Fail(403, $"Ticket priority {ticket.Priority} yêu cầu PrimaryHandler phải có tier {required}.");
        }

        var transitionResult = _stateMachine.CanTransition(ticket, TicketStatusEnum.Assigned, ActorRoleEnum.Manager, request.ManagerId);
        if (!transitionResult.IsAllowed)
            return Fail(403, transitionResult.Reason ?? "Reassignment not allowed.");

        // Swap PrimaryHandler: old PrimaryHandler → Supporter
        var oldAssignment = await _uow.TicketAssignments.GetAllAsync()
            .FirstOrDefaultAsync(a => a.TicketId == ticket.Id && !a.IsDeleted && a.Role == AssignmentRoleEnum.PrimaryHandler, ct);

        Guid? oldStaffId = oldAssignment?.StaffId;

        if (oldAssignment != null)
        {
            oldAssignment.Role = AssignmentRoleEnum.Supporter;
            _uow.TicketAssignments.UpdateAsync(oldAssignment);
        }

        // New PrimaryHandler: upsert — restore nếu đang soft-deleted, cập nhật role nếu đang active
        var newAssignment = await _uow.TicketAssignments.GetAllAsync()
            .FirstOrDefaultAsync(a => a.TicketId == ticket.Id && a.StaffId == request.NewPrimaryHandlerStaffId, ct);

        if (newAssignment != null)
        {
            newAssignment.Role = AssignmentRoleEnum.PrimaryHandler;
            newAssignment.IsDeleted = false;
            newAssignment.DeletedAt = null;
            _uow.TicketAssignments.UpdateAsync(newAssignment);
        }
        else
        {
            await _uow.TicketAssignments.AddAsync(new TicketAssignment
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                StaffId = request.NewPrimaryHandlerStaffId,
                Role = AssignmentRoleEnum.PrimaryHandler
            });
        }

        ticket.PrimaryHandlerStaffId = request.NewPrimaryHandlerStaffId;

        // TicketParticipants: old PrimaryAssignee → PreviousAssignee
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

        // Update or create TicketParticipant for new PrimaryHandler
        var newParticipant = await _uow.TicketParticipants.GetAllAsync()
            .FirstOrDefaultAsync(p => p.TicketId == ticket.Id && p.UserId == request.NewPrimaryHandlerStaffId
                && p.RemovedAt == null && !p.IsDeleted, ct);

        if (newParticipant != null)
        {
            newParticipant.ParticipantType = ParticipantTypeEnum.PrimaryAssignee;
            newParticipant.CanPost = true;
            newParticipant.CanViewInternal = true;
            _uow.TicketParticipants.UpdateAsync(newParticipant);
        }
        else
        {
            await _uow.TicketParticipants.AddAsync(new TicketParticipant
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                Ticket = ticket,
                UserId = request.NewPrimaryHandlerStaffId,
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
            newValue: request.NewPrimaryHandlerStaffId.ToString(),
            reason: request.Reason);


        // Outbox: Staff Reassigned — Sprint 6.2 NOTI-05 (#676) kèm CustomerId.
        await _producer.PublishAsync(new TicketAssignedEvent(ticket.Id, ticket.Code, request.NewPrimaryHandlerStaffId, ticket.Priority.ToString()!, ticket.CustomerId), ct);

        await _publisher.Publish(TicketService.Application.CQRS.Notification.Audit.TicketAuditTrailNotification.For(
            TicketAuditActionEnum.AssignedToStaff, ticket.Id, targetDisplay: ticket.Code,
            metadata: new Dictionary<string, object?> { ["newStaffId"] = request.NewPrimaryHandlerStaffId, ["reassign"] = true }), ct);

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
