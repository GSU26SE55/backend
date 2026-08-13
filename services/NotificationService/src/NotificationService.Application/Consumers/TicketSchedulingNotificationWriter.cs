using System.Text.Json;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.Consumers;

internal static class TicketSchedulingNotificationWriter
{
    public static async Task WriteAssignmentAsync(
        INotificationUnitOfWork unitOfWork,
        Guid ticketId,
        string code,
        Guid customerId,
        Guid primaryHandlerStaffId,
        string priority,
        DateTime? scheduledStartAtUtc,
        int scheduleVersion,
        bool workStartsImmediately,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            ticketId,
            code,
            staffId = primaryHandlerStaffId,
            customerId,
            priority,
            scheduledStartAtUtc,
            scheduleVersion,
            workStartsImmediately = workStartsImmediately,
            screen = "TicketDetail"
        });
        var businessKey = $"ticket-assigned:{scheduleVersion}";

        if (primaryHandlerStaffId != Guid.Empty)
        {
            await NotificationWriter.WriteIdempotentAsync(
                unitOfWork, [primaryHandlerStaffId], NotificationTypeEnum.TicketAssigned,
                NotificationWriter.InAppPushEmail,
                $"You have been assigned ticket {code}",
                workStartsImmediately
                    ? $"Ticket {code} (priority {priority}) has been assigned to you. Work is starting now."
                    : $"Ticket {code} (priority {priority}) is scheduled for {scheduledStartAtUtc:O}; work has not started.",
                payload, "Ticket", ticketId, businessKey, ct);
        }

        if (customerId != Guid.Empty && customerId != primaryHandlerStaffId)
        {
            await NotificationWriter.WriteIdempotentAsync(
                unitOfWork, [customerId], NotificationTypeEnum.TicketAssigned,
                NotificationWriter.InAppPushEmail,
                $"Ticket {code} now has a staff member assigned",
                workStartsImmediately
                    ? $"Your request {code} has been assigned to a technician (priority {priority})."
                    : $"Your request {code} is assigned and scheduled for {scheduledStartAtUtc:O}; work has not started.",
                payload, "Ticket", ticketId, businessKey, ct);
        }
    }

    public static Task WriteWorkStartedAsync(
        INotificationUnitOfWork unitOfWork,
        Guid ticketId,
        string code,
        Guid customerId,
        Guid primaryHandlerStaffId,
        DateTime startedAtUtc,
        int scheduleVersion,
        string activationReason,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            ticketId,
            code,
            startedAtUtc = startedAtUtc,
            scheduleVersion,
            activationReason = activationReason,
            screen = "TicketDetail"
        });
        var recipients = new[] { customerId, primaryHandlerStaffId }
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        var resumed = string.Equals(activationReason, "EarlyResume", StringComparison.Ordinal);

        return NotificationWriter.WriteIdempotentAsync(
            unitOfWork, recipients, NotificationTypeEnum.TicketWorkStarted,
            NotificationWriter.InAppPushEmail,
            resumed ? $"Work resumed on ticket {code}" : $"Work started on ticket {code}",
            resumed
                ? $"Work on ticket {code} resumed at {startedAtUtc:O}."
                : $"Work on ticket {code} started at {startedAtUtc:O}.",
            payload, "Ticket", ticketId, $"ticket-work-started:{scheduleVersion}", ct);
    }
}
