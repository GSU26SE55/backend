using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Helpers;
using TicketService.Application.Common.Models;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Notification.Audit;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
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
    private readonly ITicketActivationService _activationService;
    private readonly int _currentWindowMinutes;

    public TicketAssignCommandHandler(
        ITicketUnitOfWork uow,
        ITicketStateMachine stateMachine,
        IActivityLogger activityLogger,
        IIntegrationEventOutboxWriter producer,
        IPublisher publisher,
        ITicketActivationService activationService,
        IOptions<TicketScheduleOptions>? scheduleOptions = null)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _activityLogger = activityLogger;
        _outboxWriter = producer;
        _publisher = publisher;
        _activationService = activationService;
        _currentWindowMinutes = scheduleOptions?.Value.CurrentWindowMinutes ?? 5;
    }

    public async Task<TicketActionResponse> Handle(TicketAssignCommand request, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var schedule = TicketScheduleClassifier.Classify(request.ScheduledStartAt, nowUtc, _currentWindowMinutes);
        if (schedule.Kind == ScheduleKind.InvalidPast)
            return Fail(400, "ScheduledStartAt cannot be older than the five-minute current window.");

        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (ticket == null)
            return Fail(404, "Ticket not found.");

        var previousSchedule = ticket.ScheduledStartAtUtc;
        // Dấu hiệu "đây là ticket bảo trì định kỳ" là hạn kỳ, không phải ticket nguồn: từ khi
        // lịch chuyển sang tài sản, ticket được mở từ một kỳ bảo trì của pin chứ không còn
        // neo vào một ticket đã đóng, nên PeriodicMaintenanceSourceTicketId luôn trống và
        // dùng nó làm dấu hiệu sẽ khiến cả khối này không bao giờ chạy.
        var hasCustomerPeriodicSchedule =
            ticket.PeriodicMaintenanceDueAtUtc.HasValue &&
            ticket.PeriodicMaintenanceCustomerScheduledAtUtc.HasValue &&
            previousSchedule.HasValue;
        var customerScheduleExpired =
            hasCustomerPeriodicSchedule && previousSchedule!.Value < nowUtc;
        var replacesExpiredCustomerSchedule =
            customerScheduleExpired && schedule.ScheduledStartAtUtc != previousSchedule;

        if (hasCustomerPeriodicSchedule && !customerScheduleExpired &&
            schedule.ScheduledStartAtUtc != previousSchedule)
            return Fail(409, "The Customer-selected periodic-maintenance schedule is still valid and cannot be changed.");

        if (customerScheduleExpired && string.IsNullOrWhiteSpace(request.Notes))
            return Fail(400, "Notes are required after contacting the Customer to replace an expired schedule.");

        // Validate PrimaryHandler
        var primaryStaff = await _uow.StaffAccounts.GetAllAsync()
            .Where(s => s.AccountId == request.PrimaryHandlerStaffId && !s.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (primaryStaff == null)
            return Fail(404, "PrimaryHandler staff information not found in the system.");

        if (primaryStaff.Status != AccountStatusEnum.Active)
            return Fail(403, "The PrimaryHandler staff account is locked or disabled.");

        if (!primaryStaff.IsAvailable)
            return Fail(403, "The PrimaryHandler staff member is currently unavailable to take on new tickets.");

        if (!AssignmentRoleHelper.ValidatePrimaryHandlerTier(request.Priority, primaryStaff.SkillTier))
        {
            var required = AssignmentRoleHelper.GetTierRequirementMessage(request.Priority);
            return Fail(403, $"Ticket priority {request.Priority} requires the PrimaryHandler to have tier {required}. The assigned staff member currently has tier {primaryStaff.SkillTier}.");
        }

        var targetStatus = schedule.Kind == ScheduleKind.Future
            ? TicketStatusEnum.Pending
            : TicketStatusEnum.InProgress;
        var transitionResult = _stateMachine.CanTransition(ticket, targetStatus, ActorRoleEnum.Manager, request.ManagerId);
        if (!transitionResult.IsAllowed)
            return Fail(403, transitionResult.Reason ?? "Transition not allowed.");

        // Set in-memory cho state machine
        ticket.PrimaryHandlerStaffId = request.PrimaryHandlerStaffId;
        ticket.Priority = request.Priority;
        ticket.ScheduledStartAtUtc = schedule.ScheduledStartAtUtc;
        ticket.ScheduleVersion++;
        ticket.PendingContext = schedule.Kind == ScheduleKind.Future ? PendingContextEnum.Scheduled : null;
        ticket.PendingReason = null;

        await _uow.ExecuteInTransactionAsync(async transactionCt =>
        {
            if (ticket.ReopenCount > 0 && ticket.IsIncident && request.Priority != TicketPriorityEnum.Urgent)
            {
                await _activityLogger.LogAsync(
                    ticket.Id,
                    request.ManagerId,
                    ActorRoleEnum.Manager,
                    request.ManagerName,
                    ActivityActionEnum.IncidentDeclassified,
                    oldValue: ticket.ActiveIncidentEpisodeId?.ToString(),
                    newValue: request.Priority.ToString(),
                    reason: request.Notes);

                ticket.IsIncident = false;
                ticket.ActiveIncidentEpisodeId = null;
            }

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

            // TicketParticipant — Assigning Manager (auto-subscribe to internal chat notifications)
            // GetAllAsync() returns IQueryable — do NOT await (be-rules §3)
            var managerParticipantExists = await _uow.TicketParticipants
                .GetAllAsync()
                .AnyAsync(p => p.TicketId == ticket.Id
                    && p.UserId == request.ManagerId
                    && p.RemovedAt == null
                    && !p.IsDeleted, ct);

            if (!managerParticipantExists)
            {
                await _uow.TicketParticipants.AddAsync(new TicketParticipant
                {
                    Id = Guid.NewGuid(),
                    TicketId = ticket.Id,
                    Ticket = ticket,
                    UserId = request.ManagerId,
                    UserRole = ActorRoleEnum.Manager,
                    ParticipantType = ParticipantTypeEnum.Watcher,
                    CanPost = true,
                    CanViewInternal = true,
                    AddedByUserId = request.ManagerId,
                    AddedAt = DateTime.UtcNow
                });
            }

            // Persist assignment before activation so the transactional outbox publishes the
            // assigned/scheduled notification before the work-start notification.
            await _outboxWriter.WriteAsync(new TicketAssignedEvent(
                ticket.Id,
                ticket.Code,
                request.PrimaryHandlerStaffId,
                ticket.Priority.ToString()!,
                ticket.CustomerId,
                ticket.ScheduledStartAtUtc,
                ticket.ScheduleVersion,
                targetStatus == TicketStatusEnum.InProgress), ct);

            if (replacesExpiredCustomerSchedule && ticket.PeriodicMaintenanceDueAtUtc.HasValue)
            {
                var scheduleChanged = new PeriodicMaintenanceScheduleChangedEvent(
                    ticket.Id,
                    ticket.Code,
                    ticket.BatteryAssetId,
                    ticket.CustomerId,
                    previousSchedule,
                    ticket.ScheduledStartAtUtc!.Value,
                    ticket.ScheduleVersion,
                    nameof(ActorRoleEnum.Manager),
                    request.ManagerId,
                    request.Notes,
                    ticket.PeriodicMaintenanceDueAtUtc.Value,
                    ticket.PeriodicMaintenanceDueAtUtc.Value < nowUtc)
                {
                    Id = DeterministicEventId.From(
                        ticket.Id,
                        $"periodic-maintenance-schedule:{ticket.ScheduleVersion}")
                };

                await _outboxWriter.WriteAsync(scheduleChanged, transactionCt);
                await _activityLogger.LogAsync(
                    ticket.Id,
                    request.ManagerId,
                    ActorRoleEnum.Manager,
                    request.ManagerName,
                    ActivityActionEnum.PeriodicMaintenanceScheduleChanged,
                    previousSchedule?.ToString("O"),
                    ticket.ScheduledStartAtUtc.Value.ToString("O"),
                    request.Notes);
            }

            if (targetStatus == TicketStatusEnum.Pending)
            {
                await _stateMachine.ExecuteAsync(ticket, targetStatus, new TransitionContext
                {
                    ActorUserId = request.ManagerId,
                    ActorRole = ActorRoleEnum.Manager,
                    ActorDisplayName = request.ManagerName!
                }, ct);
            }
            else
            {
                var activation = await _activationService.ActivateAsync(new ActivationRequest(
                    ticket,
                    request.PrimaryHandlerStaffId,
                    ticket.ScheduleVersion,
                    nowUtc,
                    ActivationReason.Immediate,
                    request.ManagerId,
                    ActorRoleEnum.Manager,
                    request.ManagerName ?? "Manager"), ct);
                if (!activation.Activated)
                    throw new InvalidOperationException(activation.Conflict ?? "Ticket activation failed.");
            }

            await _activityLogger.LogAsync(
                ticket.Id,
                request.ManagerId,
                ActorRoleEnum.Manager,
                request.ManagerName!,
                ActivityActionEnum.StaffAssigned,
                oldValue: null,
                newValue: request.PrimaryHandlerStaffId.ToString(),
                reason: request.Notes);

            await _publisher.Publish(TicketAuditTrailNotification.For(
                TicketAuditActionEnum.AssignedToStaff, ticket.Id, targetDisplay: ticket.Code,
                metadata: new Dictionary<string, object?> { ["staffId"] = request.PrimaryHandlerStaffId }), ct);

            await _uow.SaveChangesAsync(ct);

        }, ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Ticket assigned successfully.",
            Data = new TicketActionDTO
            {
                Id = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status,
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
