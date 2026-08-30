using TicketService.Application.DTOs.Response.SLA;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.Common.Utils;

public static class TicketQueryHelper
{
    /// <param name="t">Entity Ticket nguồn.</param>
    /// <param name="slaCalculator">Business clock dùng để tính SLA còn lại.</param>
    /// <param name="atUtc">Thời điểm UTC dùng cho phép tính SLA.</param>
    /// <param name="hasUnreadChat">Ticket có tin nhắn chưa đọc với actor hiện tại.</param>
    /// <param name="staffNames">
    /// Map StaffId → FullName để điền <c>TicketAssignmentDTO.StaffName</c>.
    /// Truyền null thì StaffName để trống (FE tự fallback sang StaffId).
    /// </param>
    /// <param name="canViewSlaTimer">
    /// GH-1242 — SLA là chỉ số nội bộ của Staff. Customer chỉ được thấy
    /// <c>ExpectedCompletionAtUtc</c>; truyền false để ẩn cả block <c>SlaTimer</c>
    /// (gồm BreachAt, WarningSentAt, RemainingPercent).
    /// </param>
    internal static TicketDTO MapToTicketDTO(
        Ticket t,
        ISlaCalculator slaCalculator,
        DateTime atUtc,
        bool hasUnreadChat = false,
        IReadOnlyDictionary<Guid, string>? staffNames = null,
        bool canViewSlaTimer = true) => new()
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
            PeriodicMaintenanceDueAtUtc = t.PeriodicMaintenanceDueAtUtc,
            PeriodicMaintenanceScheduleDeadlineAtUtc = t.PeriodicMaintenanceScheduleDeadlineAtUtc,
            PendingContext = t.PendingContext,
            PendingReason = t.PendingReason,
            ActiveIncidentEpisodeId = t.ActiveIncidentEpisodeId?.ToString(),
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
            SlaTimer = canViewSlaTimer ? MapToSlaTimerDTO(t.SlaTimer, slaCalculator, atUtc, t.Status) : null,
            ExpectedCompletionAtUtc = t.SlaTimer?.DueAt,
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

    internal static SlaTimerDTO? MapToSlaTimerDTO(
        SlaTimer? sla,
        ISlaCalculator slaCalculator,
        DateTime atUtc,
        TicketStatusEnum ticketStatus = TicketStatusEnum.Open)
    {
        if (sla is null)
            return null;

        var isStoppedOrTerminal = sla.Status == SlaTimerStatusEnum.Stopped || TicketStatusGroups.Terminal.Contains(ticketStatus);
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
            RemainingPercent = isStoppedOrTerminal ? 0d : ComputeRemainingPercent(slaCalculator, sla, atUtc),
            SlaWorkingDays = slaCalculator.GetSlaWorkingDays(sla.Priority),
            SlaWorkingHours = slaCalculator.GetSlaHours(sla.Priority),
            RemainingWorkingMinutes = isStoppedOrTerminal ? 0 : ComputeRemainingWorkingMinutes(slaCalculator, sla, atUtc)
        };
    }

    /// <summary>
    /// Số phút làm việc còn lại tới <c>timer.DueAt</c> — đồng hồ đếm ngược phía Staff.
    /// Dùng cùng quy ước đóng băng khi Paused như <see cref="ComputeRemainingPercent(ISlaCalculator, SlaTimer, DateTime)"/>
    /// để hai con số không lệch nhau.
    /// </summary>
    public static int ComputeRemainingWorkingMinutes(
        ISlaCalculator slaCalculator,
        SlaTimer timer,
        DateTime atUtc)
    {
        if (timer.Status is not (SlaTimerStatusEnum.Running or SlaTimerStatusEnum.Paused))
            return 0;

        var observedAt = timer.Status == SlaTimerStatusEnum.Paused && timer.CurrentPauseStartedAt.HasValue
            ? timer.CurrentPauseStartedAt.Value
            : atUtc;
        return (int)slaCalculator.GetWorkingMinutesBetween(observedAt, timer.DueAt);
    }

    /// <summary>
    /// % SLA còn lại tại thời điểm <paramref name="atUtc"/>.
    /// Khi Paused, timer không chạy nên % được đóng băng tại <c>timer.CurrentPauseStartedAt</c>
    /// thay vì tính theo <paramref name="atUtc"/> — tránh hiện 0% giả (DueAt chưa cộng bù thời gian pause,
    /// cộng bù chỉ xảy ra lúc Resume) khiến ticket trông như đã quá hạn dù SLA đang đứng yên.
    /// Dùng chung cho SlaTimerDTO và dashboard stats để hai nơi không lệch công thức.
    /// </summary>
    public static double ComputeRemainingPercent(
        ISlaCalculator slaCalculator,
        SlaTimer timer,
        DateTime atUtc) => slaCalculator.GetRemainingPercent(timer, atUtc);

    public static double ComputeRemainingPercent(
        ISlaCalculator slaCalculator,
        SlaTimerStatusEnum status,
        TicketPriorityEnum priority,
        DateTime dueAt,
        DateTime? currentPauseStartedAt,
        DateTime atUtc) => slaCalculator.GetRemainingPercent(new SlaTimer
        {
            Status = status,
            Priority = priority,
            DueAt = dueAt,
            CurrentPauseStartedAt = currentPauseStartedAt
        }, atUtc);

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

    /// <summary>GH-1242 — chỉ nội bộ mới thấy SLA timer; Customer chỉ thấy ngày dự kiến hoàn thành.</summary>
    public static bool CanViewSlaTimer(IReadOnlyCollection<string> actorRoles)
        => HasAnyRole(actorRoles, "Admin", "Manager", "Staff");

    /// <summary>
    /// Lọc danh sách ticket theo tình trạng SLA. Trả về IQueryable nên điều kiện dịch được
    /// sang SQL — không kéo cả bảng về rồi lọc trong bộ nhớ.
    ///
    /// Đặt ở đây (không viết lặp trong từng handler) để Admin/Manager và mọi danh sách sau này
    /// hiểu "Warning" y hệt nhau. Ticket chưa có SlaTimer luôn bị loại ở cả ba nhánh: chưa
    /// chạy đồng hồ thì không thể pause/warning/breach.
    /// </summary>
    public static IQueryable<Ticket> FilterBySla(IQueryable<Ticket> query, SlaFilterEnum? sla) => sla switch
    {
        SlaFilterEnum.Paused => query.Where(t =>
            t.SlaTimer != null && t.SlaTimer.Status == SlaTimerStatusEnum.Paused),

        // Còn Running: cảnh báo đã bắn nhưng DueAt CHƯA về 0. Không chỉ xét WarningSentAt —
        // trường đó vẫn còn nguyên sau khi timer chuyển Breached, nên bỏ điều kiện Running thì
        // ticket đã quá hạn sẽ lọt vào cả hai bộ lọc.
        SlaFilterEnum.Warning => query.Where(t =>
            t.SlaTimer != null
            && t.SlaTimer.Status == SlaTimerStatusEnum.Running
            && t.SlaTimer.WarningSentAt != null),

        // "Về 0": SlaTimerBackgroundService đóng dấu Breached đúng lúc DueAt <= now.
        // Lọc theo Status chứ không so DueAt với thời điểm hiện tại — timer Paused có thể
        // vượt DueAt mà vẫn chưa vi phạm (DueAt chỉ được cộng bù lúc Resume).
        SlaFilterEnum.Breached => query.Where(t =>
            t.SlaTimer != null && t.SlaTimer.Status == SlaTimerStatusEnum.Breached),

        _ => query
    };

    /// <summary>
    /// Lọc theo nguồn tạo. Thứ tự điều kiện phải KHỚP với thứ tự ưu tiên FE dùng để gắn nhãn,
    /// nếu không một ticket sẽ hiện nhãn này mà lại lọt vào bộ lọc kia.
    ///
    /// Origin = System bị hai luồng dùng chung (sự cố môi trường, bảo trì định kỳ) cộng thêm
    /// cascade risk, nên AiPredicted phải loại trừ tường minh hai nhóm kia — không có cột nào
    /// đánh dấu riêng cascade risk.
    /// </summary>
    public static IQueryable<Ticket> FilterBySource(IQueryable<Ticket> query, TicketSourceFilterEnum? source) => source switch
    {
        TicketSourceFilterEnum.Customer => query.Where(t =>
            t.Origin == TicketOriginEnum.ManualByCustomer),

        // AI dự đoán: alert bất thường do AI module chấm, hoặc điểm cascade risk cao
        // (System nhưng không phải sự cố môi trường / bảo trì định kỳ).
        TicketSourceFilterEnum.AiPredicted => query.Where(t =>
            t.Origin == TicketOriginEnum.AutoFromAlert
            || (t.Origin == TicketOriginEnum.System
                && t.EnvironmentalIncidentId == null
                && t.PeriodicMaintenanceDueAtUtc == null)),

        // Xét trước Origin: ticket sự cố môi trường luôn có IncidentId, và đây là dấu hiệu
        // duy nhất tách nó khỏi luồng System còn lại.
        TicketSourceFilterEnum.Environmental => query.Where(t =>
            t.EnvironmentalIncidentId != null),

        TicketSourceFilterEnum.PeriodicMaintenance => query.Where(t =>
            t.PeriodicMaintenanceDueAtUtc != null),

        _ => query
    };

    private static bool HasAnyRole(IReadOnlyCollection<string> roles, params string[] check)
        => check.Any(r => HasRole(roles, r));

    private static bool HasRole(IReadOnlyCollection<string> roles, string role)
        => roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
}
