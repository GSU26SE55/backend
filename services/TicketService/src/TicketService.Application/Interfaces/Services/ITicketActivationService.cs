using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.Interfaces.Services;

public interface ITicketActivationService
{
    Task<ActivationResult> ActivateAsync(ActivationRequest request, CancellationToken ct);
    Task CompleteSlaAsync(Ticket ticket, CancellationToken ct);
    Task StartCorrectionSlaAsync(Ticket ticket, DateTime nowUtc, CancellationToken ct);
    Task StopSlaAsync(Ticket ticket, CancellationToken ct);
}

public record ActivationRequest(
    Ticket Ticket,
    Guid PrimaryHandlerStaffId,
    int ExpectedScheduleVersion,
    DateTime NowUtc,
    ActivationReason Reason,
    Guid ActorUserId,
    ActorRoleEnum ActorRole,
    string ActorDisplayName,
    string? UserReason = null);

public enum ActivationReason { Immediate, ScheduledDue, EarlyResume }
public record ActivationResult(bool Activated, string? Conflict = null);
