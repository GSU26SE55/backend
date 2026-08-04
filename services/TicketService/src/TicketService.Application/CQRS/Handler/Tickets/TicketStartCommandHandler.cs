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

public class TicketStartCommandHandler : IRequestHandler<TicketStartCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketStateMachine _stateMachine;
    private readonly IActivityLogger _activityLogger;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-26

    public TicketStartCommandHandler(
        ITicketUnitOfWork uow,
        ITicketStateMachine stateMachine,
        IActivityLogger activityLogger,
        IIntegrationEventOutboxWriter producer,
        IPublisher publisher)
    {
        _uow = uow;
        _stateMachine = stateMachine;
        _activityLogger = activityLogger;
        _outboxWriter = producer;
        _publisher = publisher;
    }

    public async Task<TicketActionResponse> Handle(TicketStartCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (ticket == null)
            return Fail(404, "Ticket not found.");

        if (ticket.PrimaryHandlerStaffId == null && _uow.TicketAssignments != null)
        {
            ticket.PrimaryHandlerStaffId = await _uow.TicketAssignments.GetAllAsync()
                .Where(a => a.TicketId == ticket.Id && !a.IsDeleted && a.Role == AssignmentRoleEnum.PrimaryHandler)
                .Select(a => (Guid?)a.StaffId)
                .FirstOrDefaultAsync(ct);
        }

        var transitionResult = _stateMachine.CanTransition(ticket, TicketStatusEnum.InProgress, ActorRoleEnum.Staff, request.StaffId);
        if (!transitionResult.IsAllowed)
            return Fail(403, transitionResult.Reason ?? "Cannot start work.");

        await _stateMachine.ExecuteAsync(ticket, TicketStatusEnum.InProgress, new TransitionContext
        {
            ActorUserId = request.StaffId,
            ActorRole = ActorRoleEnum.Staff,
            ActorDisplayName = request.StaffName!
        }, ct);

        // TỰ ĐỘNG TẠO MAINTENANCE LOG KHI START WORK
        // Kiểm tra xem đã có log nào chưa xong không (đề phòng)
        var activeLogExists = await _uow.MaintenanceLogs.GetAllAsync()
            .AnyAsync(m => m.TicketId == ticket.Id && m.CompletedAt == null && !m.IsDeleted, ct);

        if (!activeLogExists)
        {
            var log = new MaintenanceLog
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                Ticket = ticket,
                StaffId = request.StaffId,
                LogType = request.LogType ?? MaintenanceLogTypeEnum.OnSite,
                Summary = "Đang thực hiện...",
                StartedAt = DateTime.UtcNow,
                // CheckInLatitude = request.Latitude,
                // CheckInLongitude = request.Longitude,
                CheckInAt = DateTime.UtcNow
            };
            await _uow.MaintenanceLogs.AddAsync(log);
        }

        await _activityLogger.LogAsync(
            ticket.Id,
            request.StaffId,
            ActorRoleEnum.Staff,
            request.StaffName!,
            ActivityActionEnum.StatusChanged,
            oldValue: "Assigned",
            newValue: "InProgress");

        // Outbox: Status Changed
        await _outboxWriter.WriteAsync(new TicketStatusChangedIntegrationEvent(ticket.Id, ticket.Code, TicketStatusEnum.Assigned, TicketStatusEnum.InProgress), ct);

        // Sprint 6.2 NOTI-07 (#678) — bản SharedContracts để Customer biết Staff đã bắt tay xử lý.
        await _outboxWriter.WriteAsync(new TicketStatusChangedEvent(
            ticket.Id, ticket.Code, ticket.CustomerId, ticket.PrimaryHandlerStaffId,
            (int)TicketStatusEnum.Assigned, (int)TicketStatusEnum.InProgress,
            nameof(TicketStatusEnum.Assigned), nameof(TicketStatusEnum.InProgress)), ct);

        // #AUDIT-26
        await _publisher.Publish(TicketService.Application.CQRS.Notification.Audit.TicketAuditTrailNotification.For(
            TicketAuditActionEnum.StateTransitioned, ticket.Id, targetDisplay: ticket.Code,
            metadata: new Dictionary<string, object?> { ["to"] = "InProgress" }), ct);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Work started.",
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
