using TicketService.Application.DTOs.Response.SLA;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.Helpers;

public static class TicketQueryHelper
{
    internal static TicketDTO MapToTicketDTO(Ticket t) => new()
    {
        Id = t.Id.ToString(),
        Code = t.Code,
        BatteryAssetId = t.BatteryAssetId.ToString(),
        CustomerId = t.CustomerId.ToString(),
        AssignedStaffId = t.AssignedStaffId.HasValue ? t.AssignedStaffId.Value.ToString() : null,
        Title = t.Title,
        Category = t.Category,
        Priority = t.Priority,
        ImpactScope = t.ImpactScope,
        UrgencyLevel = t.UrgencyLevel,
        Status = t.Status,
        Origin = t.Origin,
        ReopenCount = t.ReopenCount,
        IsIncident = t.IsIncident,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
        SlaTimer = MapToSlaTimerDTO(t.SlaTimer)
    };

    internal static SlaTimerDTO? MapToSlaTimerDTO(SlaTimer? sla)
    {
        if (sla is null)
            return null;
        return new SlaTimerDTO
        {
            Id = sla.Id.ToString(),
            Priority = sla.Priority,
            StartedAt = sla.StartedAt,
            DueAt = sla.DueAt,
            OriginalDueAt = sla.OriginalDueAt,
            TotalPausedMinutes = sla.TotalPausedMinutes,
            WarningSentAt = sla.WarningSentAt,
            BreachAt = sla.BreachAt,
            Status = sla.Status,
            RemainingPercent = sla.Status == SlaTimerStatusEnum.Running
                ? (sla.DueAt != sla.StartedAt
                    ? Math.Max(0, (sla.DueAt - DateTime.UtcNow).TotalMinutes /
                        (sla.DueAt - sla.StartedAt).TotalMinutes * 100)
                    : 0d)
                : 0d
        };
    }

    public static bool CanAccessTicket(
        Guid customerId,
        Guid? assignedStaffId,
        Guid? actorUserId,
        IReadOnlyCollection<string> actorRoles)
    {
        if (HasAnyRole(actorRoles, "Admin", "Manager"))
            return true;
        if (!actorUserId.HasValue)
            return false;
        if (HasRole(actorRoles, "Customer") && customerId == actorUserId.Value)
            return true;
        return HasRole(actorRoles, "Staff") && assignedStaffId == actorUserId.Value;
    }

    public static bool CanViewInternalChats(IReadOnlyCollection<string> actorRoles)
        => HasAnyRole(actorRoles, "Admin", "Manager", "Staff");

    public static bool IsManagerOrAdmin(IReadOnlyCollection<string> actorRoles)
        => HasAnyRole(actorRoles, "Admin", "Manager");

    private static bool HasAnyRole(IReadOnlyCollection<string> roles, params string[] check)
        => check.Any(r => HasRole(roles, r));

    private static bool HasRole(IReadOnlyCollection<string> roles, string role)
        => roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
}
