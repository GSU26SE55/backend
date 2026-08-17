using TicketService.Application.DTOs.Response.SLA;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.Common.Utils;

public static class TicketQueryHelper
{
    /// <param name="t">Entity Ticket nguồn.</param>
    /// <param name="hasUnreadChat">Ticket có tin nhắn chưa đọc với actor hiện tại.</param>
    /// <param name="staffNames">
    /// Map StaffId → FullName để điền <c>TicketAssignmentDTO.StaffName</c>.
    /// Truyền null thì StaffName để trống (FE tự fallback sang StaffId).
    /// </param>
    internal static TicketDTO MapToTicketDTO(
        Ticket t,
        bool hasUnreadChat = false,
        IReadOnlyDictionary<Guid, string>? staffNames = null) => new()
        {
            Id = t.Id.ToString(),
            Code = t.Code,
            // Sprint Bonus NS-22 (#662) — ticket site-level (env incident, Origin=System) có
            // BatteryAssetId = Guid.Empty → trả chuỗi rỗng (contract DTO: "không liên quan pin cụ thể").
            BatteryAssetId = t.BatteryAssetId == Guid.Empty ? string.Empty : t.BatteryAssetId.ToString(),
            BatteryAssetIds = t.BatteryAssets.Select(b => b.BatteryAssetId.ToString()).ToList(),
            CustomerId = t.CustomerId.ToString(),
            Assignments = t.Assignments
            .Where(a => !a.IsDeleted)
            .Select(a => new TicketAssignmentDTO
            {
                StaffId = a.StaffId.ToString(),
                Role = a.Role,
                StaffName = staffNames != null && staffNames.TryGetValue(a.StaffId, out var n) ? n : null,
            })
            .ToList(),
            Title = t.Title,
            Category = t.Category,
            Priority = t.Priority,
            ImpactScope = t.ImpactScope,
            UrgencyLevel = t.UrgencyLevel,
            Status = t.Status,
            Origin = t.Origin,
            ReopenCount = t.ReopenCount,
            IsIncident = t.IsIncident,
            EnvironmentalIncidentId = t.EnvironmentalIncidentId.HasValue
                ? t.EnvironmentalIncidentId.Value.ToString()
                : null,
            ScheduledStartAtUtc = t.ScheduledStartAtUtc,
            ScheduleVersion = t.ScheduleVersion,
            PendingContext = t.PendingContext,
            PendingReason = t.PendingReason,
            ActiveIncidentEpisodeId = t.ActiveIncidentEpisodeId?.ToString(),
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
            SlaTimer = MapToSlaTimerDTO(t.SlaTimer),
            HasUnreadChat = hasUnreadChat,
            DetectedAt = t.DetectedAt,
            BatterySerialNumber = t.BatterySerialNumber,
            AiVerifyStatus = t.AiVerifyStatus,
            AiVerifyScore = t.AiVerifyScore,
            AiVerifyReason = t.AiVerifyReason,
            SuspectedDuplicateOfTicketId = t.SuspectedDuplicateOfTicketId?.ToString(),
            DuplicateReason = t.DuplicateReason,
            MergedIntoTicketId = t.MergedIntoTicketId?.ToString(),
            CloseReason = t.CloseReason
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
            PauseEpisodesCount = sla.PauseEpisodesCount,
            WarningSentAt = sla.WarningSentAt,
            BreachAt = sla.BreachAt,
            Status = sla.Status,
            RemainingPercent = ComputeRemainingPercent(sla.Status, sla.StartedAt, sla.DueAt, DateTime.UtcNow)
        };
    }

    /// <summary>
    /// % SLA còn lại tại thời điểm <paramref name="atUtc"/> — 0 nếu timer không ở trạng thái Running hoặc đã quá hạn.
    /// Dùng chung cho SlaTimerDTO và dashboard stats để hai nơi không lệch công thức.
    /// </summary>
    public static double ComputeRemainingPercent(SlaTimerStatusEnum status, DateTime startedAt, DateTime dueAt, DateTime atUtc)
    {
        if (status != SlaTimerStatusEnum.Running || dueAt == startedAt)
            return 0d;
        return Math.Max(0, (dueAt - atUtc).TotalMinutes / (dueAt - startedAt).TotalMinutes * 100);
    }

    public static bool CanAccessTicket(
        Guid customerId,
        Guid? primaryHandlerStaffId,
        Guid? actorUserId,
        IReadOnlyCollection<string> actorRoles)
    {
        if (HasAnyRole(actorRoles, "Admin", "Manager"))
            return true;
        if (!actorUserId.HasValue)
            return false;
        if (HasRole(actorRoles, "Customer") && customerId == actorUserId.Value)
            return true;
        return HasRole(actorRoles, "Staff") && primaryHandlerStaffId == actorUserId.Value;
    }

    /// <summary>Overload có xét active participant row (#522) — actor có row active trên ticket cũng được truy cập dù không phải Customer chính/Staff assigned.</summary>
    public static bool CanAccessTicket(
        Guid customerId,
        Guid? primaryHandlerStaffId,
        Guid? actorUserId,
        IReadOnlyCollection<string> actorRoles,
        IReadOnlyCollection<Guid> activeParticipantUserIds)
    {
        if (CanAccessTicket(customerId, primaryHandlerStaffId, actorUserId, actorRoles))
            return true;
        return actorUserId.HasValue && activeParticipantUserIds.Contains(actorUserId.Value);
    }

    public static bool CanViewInternalChats(IReadOnlyCollection<string> actorRoles)
        => HasAnyRole(actorRoles, "Admin", "Manager", "Staff");

    /// <summary>Overload có xét participant.CanViewInternal (#522) — participant được cấp quyền xem internal dù không phải Staff/Manager/Admin.</summary>
    public static bool CanViewInternalChats(IReadOnlyCollection<string> actorRoles, bool participantCanViewInternal)
        => CanViewInternalChats(actorRoles) || participantCanViewInternal;

    public static bool IsManagerOrAdmin(IReadOnlyCollection<string> actorRoles)
        => HasAnyRole(actorRoles, "Admin", "Manager");

    private static bool HasAnyRole(IReadOnlyCollection<string> roles, params string[] check)
        => check.Any(r => HasRole(roles, r));

    private static bool HasRole(IReadOnlyCollection<string> roles, string role)
        => roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
}
