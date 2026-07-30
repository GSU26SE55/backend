using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Helpers;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Notification.Audit;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.StateMachine;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketAssignCommandHandler : IRequestHandler<TicketAssignCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly IPublisher _publisher;
    private readonly ISlaCalculator _slaCalculator;

    public TicketAssignCommandHandler(
        ITicketUnitOfWork uow,
        ITicketStateMachine stateMachine,
        IActivityLogger activityLogger,
        IIntegrationEventOutboxWriter producer,
        IPublisher publisher,
        ISlaCalculator slaCalculator)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _activityLogger = activityLogger;
        _outboxWriter = producer;
        _publisher = publisher;
        _slaCalculator = slaCalculator;
    }

    public async Task<TicketActionResponse> Handle(TicketAssignCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (ticket == null)
            return Fail(404, "Ticket not found.");

        // Validate PrimaryHandler
        var primaryStaff = await _uow.StaffAccounts.GetAllAsync()
            .Where(s => s.AccountId == request.PrimaryHandlerStaffId && !s.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (primaryStaff == null)
            return Fail(404, "Không tìm thấy thông tin nhân viên PrimaryHandler trong hệ thống.");

        if (primaryStaff.Status != AccountStatusEnum.Active)
            return Fail(403, "Tài khoản nhân viên PrimaryHandler đang bị khóa hoặc vô hiệu hóa.");

        if (!primaryStaff.IsAvailable)
            return Fail(403, "Nhân viên PrimaryHandler hiện đang không sẵn sàng nhận ticket mới.");

        // Tier validation — PrimaryHandler phải đủ tier theo priority của ticket
        if (ticket.Priority.HasValue && !AssignmentRoleHelper.ValidatePrimaryHandlerTier(ticket.Priority.Value, primaryStaff.SkillTier))
        {
            var required = AssignmentRoleHelper.GetTierRequirementMessage(ticket.Priority.Value);
            return Fail(403, $"Ticket priority {ticket.Priority} yêu cầu PrimaryHandler phải có tier {required}. Nhân viên được chỉ định hiện có tier {primaryStaff.SkillTier}.");
        }

        var transitionResult = _stateMachine.CanTransition(ticket, TicketStatusEnum.Assigned, ActorRoleEnum.Manager, request.ManagerId);
        if (!transitionResult.IsAllowed)
            return Fail(403, transitionResult.Reason ?? "Transition not allowed.");

        // Set in-memory cho state machine
        ticket.PrimaryHandlerStaffId = request.PrimaryHandlerStaffId;

        await _uow.ExecuteInTransactionAsync(async transactionCt =>
        {
            // Upsert TicketAssignment — PrimaryHandler (restore soft-deleted row nếu tồn tại)
            var primaryAssignment = await _uow.TicketAssignments.GetAllAsync()
                .FirstOrDefaultAsync(a => a.TicketId == ticket.Id && a.StaffId == request.PrimaryHandlerStaffId, ct);
            if (primaryAssignment != null)
            {
                primaryAssignment.Role = AssignmentRoleEnum.PrimaryHandler;
                primaryAssignment.IsDeleted = false;
                primaryAssignment.DeletedAt = null;
                _uow.TicketAssignments.UpdateAsync(primaryAssignment);
            }
            else
            {
                await _uow.TicketAssignments.AddAsync(new TicketAssignment
                {
                    Id = Guid.NewGuid(),
                    TicketId = ticket.Id,
                    StaffId = request.PrimaryHandlerStaffId,
                    Role = AssignmentRoleEnum.PrimaryHandler
                });
            }

            // Upsert TicketAssignments — Supporters (restore soft-deleted row nếu tồn tại)
            foreach (var supporterId in request.SupporterStaffIds)
            {
                var supporterAssignment = await _uow.TicketAssignments.GetAllAsync()
                    .FirstOrDefaultAsync(a => a.TicketId == ticket.Id && a.StaffId == supporterId, ct);
                if (supporterAssignment != null)
                {
                    supporterAssignment.Role = AssignmentRoleEnum.Supporter;
                    supporterAssignment.IsDeleted = false;
                    supporterAssignment.DeletedAt = null;
                    _uow.TicketAssignments.UpdateAsync(supporterAssignment);
                }
                else
                {
                    await _uow.TicketAssignments.AddAsync(new TicketAssignment
                    {
                        Id = Guid.NewGuid(),
                        TicketId = ticket.Id,
                        StaffId = supporterId,
                        Role = AssignmentRoleEnum.Supporter
                    });
                }
            }

            // TicketParticipant — PrimaryAssignee (#528). An assignee may already be an
            // active participant, so avoid violating the active-participant unique index.
            var primaryParticipantExists = await _uow.TicketParticipants.GetAllAsync()
                .AnyAsync(p => p.TicketId == ticket.Id
                    && p.UserId == request.PrimaryHandlerStaffId
                    && p.RemovedAt == null
                    && !p.IsDeleted, ct);

            if (!primaryParticipantExists)
            {
                await _uow.TicketParticipants.AddAsync(new TicketParticipant
                {
                    Id = Guid.NewGuid(),
                    TicketId = ticket.Id,
                    Ticket = ticket,
                    UserId = request.PrimaryHandlerStaffId,
                    UserRole = ActorRoleEnum.Staff,
                    ParticipantType = ParticipantTypeEnum.PrimaryAssignee,
                    CanPost = true,
                    CanViewInternal = true,
                    AddedByUserId = request.ManagerId,
                    AddedAt = DateTime.UtcNow
                });
            }

            // TicketParticipants — Supporters (Collaborator)
            foreach (var supporterId in request.SupporterStaffIds)
            {
                var exists = await _uow.TicketParticipants.GetAllAsync()
                    .AnyAsync(p => p.TicketId == ticket.Id && p.UserId == supporterId && p.RemovedAt == null && !p.IsDeleted, ct);
                if (!exists)
                {
                    await _uow.TicketParticipants.AddAsync(new TicketParticipant
                    {
                        Id = Guid.NewGuid(),
                        TicketId = ticket.Id,
                        Ticket = ticket,
                        UserId = supporterId,
                        UserRole = ActorRoleEnum.Staff,
                        ParticipantType = ParticipantTypeEnum.Collaborator,
                        CanPost = true,
                        CanViewInternal = true,
                        AddedByUserId = request.ManagerId,
                        AddedAt = DateTime.UtcNow
                    });
                }
            }

            await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.Assigned, new TransitionContext
            {
                ActorUserId = request.ManagerId,
                ActorRole = ActorRoleEnum.Manager,
                ActorDisplayName = request.ManagerName!
            }, ct);

            // Sprint Bonus NS-12 (#656, R1) — tạo SlaTimer idempotent
            await EnsureSlaTimerAsync(ticket, ct);

            await _activityLogger.LogAsync(
                ticket.Id,
                request.ManagerId,
                ActorRoleEnum.Manager,
                    request.ManagerName!,
                ActivityActionEnum.StaffAssigned,
                oldValue: null,
                newValue: request.PrimaryHandlerStaffId.ToString(),
                reason: request.Notes);

            await _outboxWriter.WriteAsync(new TicketAssignedEvent(ticket.Id, ticket.Code, request.PrimaryHandlerStaffId, ticket.Priority.ToString()!), ct);

            await _publisher.Publish(TicketAuditTrailNotification.For(
                TicketAuditActionEnum.AssignedToStaff, ticket.Id, targetDisplay: ticket.Code,
                metadata: new Dictionary<string, object?> { ["staffId"] = request.PrimaryHandlerStaffId }), ct);

        }, ct);

<<<<<<< HEAD
        // Sprint Bonus NS-12 (#656, R1) — tạo SlaTimer idempotent
        await EnsureSlaTimerAsync(ticket, ct);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.ManagerId,
            ActorRoleEnum.Manager,
            request.ManagerName ?? "Manager",
            ActivityActionEnum.StaffAssigned,
            oldValue: null,
            newValue: request.PrimaryHandlerStaffId.ToString(),
            reason: request.Notes);

        // Sprint 6.2 NOTI-05 (#676) — kèm CustomerId để NotificationService notify được Customer.
        await _producer.WriteAsync(
            new TicketAssignedEvent(ticket.Id, ticket.Code, request.PrimaryHandlerStaffId, ticket.Priority.ToString()!, ticket.CustomerId), ct);

        await _publisher.Publish(TicketAuditTrailNotification.For(
            TicketAuditActionEnum.AssignedToStaff, ticket.Id, targetDisplay: ticket.Code,
            metadata: new Dictionary<string, object?> { ["staffId"] = request.PrimaryHandlerStaffId }), ct);

        await _uow.SaveChangesAsync(ct);

=======
>>>>>>> 2aaa4b15 (feat: resole duplicate ticket on merged Closes #699)
        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Ticket assigned successfully.",
            Data = new TicketActionDTO
            {
                Id = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
            }
        };
    }

    private async Task EnsureSlaTimerAsync(TicketService.Domain.Entities.Ticket ticket, CancellationToken ct)
    {
        if (ticket.Priority is null)
            return;

        var hasTimer = await _uow.SlaTimers.GetAllAsync()
            .AnyAsync(t => t.TicketId == ticket.Id, ct);
        if (hasTimer)
            return;

        var now = DateTime.UtcNow;
        var dueAt = _slaCalculator.CalculateDueDate(now, ticket.Priority.Value);

        await _uow.SlaTimers.AddAsync(new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Priority = ticket.Priority.Value,
            StartedAt = now,
            DueAt = dueAt,
            OriginalDueAt = dueAt,
            Status = SlaTimerStatusEnum.Running
        });
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
