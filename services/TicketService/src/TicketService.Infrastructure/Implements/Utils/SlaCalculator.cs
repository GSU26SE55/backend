using Microsoft.Extensions.Options;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Implements.Utils;

public class SlaCalculator : ISlaCalculator
{
    private readonly TimeZoneInfo _timeZone;
    private readonly TimeSpan _start;
    private readonly TimeSpan _end;
    private readonly HashSet<DayOfWeek> _workingDays;
    private readonly int _workingMinutesPerDay;
    private readonly ISlaBusinessCalendarProvider? _calendarProvider;

    public SlaCalculator() : this(Options.Create(new SlaBusinessHoursOptions()), null)
    {
    }

    public SlaCalculator(IOptions<SlaBusinessHoursOptions> options) : this(options, null)
    {
    }

    public SlaCalculator(
        IOptions<SlaBusinessHoursOptions> options,
        ISlaBusinessCalendarProvider? calendarProvider)
    {
        var value = options.Value;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(value.TimeZoneId);
        _start = value.Start;
        _end = value.End;
        _workingDays = value.WorkingDays.ToHashSet();
        _workingMinutesPerDay = (int)(_end - _start).TotalMinutes;
        _calendarProvider = calendarProvider;
    }

    public DateTime CalculateSlaDueDate(Ticket ticket)
    {
        if (ticket.Priority == null)
            throw new ArgumentNullException(nameof(ticket.Priority), "Ticket priority cannot be null to calculate SLA due date.");

        return CalculateDueDate(ticket.CreatedAt, ticket.Priority.Value);
    }

    public DateTime CalculateResponseDueDate(DateTime startedAt, TicketPriorityEnum priority)
    {
        var hours = priority switch
        {
            TicketPriorityEnum.P1Critical => 4,
            TicketPriorityEnum.P2High => 24,
            TicketPriorityEnum.P3Normal => 72,
            _ => 72
        };
        return EnsureUtc(startedAt).AddHours(hours);
    }

    public DateTime CalculateDueDate(DateTime startedAt, TicketPriorityEnum priority)
        => AddWorkingMinutes(startedAt, GetSlaMinutes(priority));

    public int GetSlaWorkingDays(TicketPriorityEnum priority)
    {
        return priority switch
        {
            TicketPriorityEnum.P1Critical => 14,
            TicketPriorityEnum.P2High => 3,
            TicketPriorityEnum.P3Normal => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(priority),
                $"Priority value {priority} is not supported for SLA calculation.")
        };
    }

    public int GetSlaHours(TicketPriorityEnum priority) => GetSlaMinutes(priority) / 60;

    // Budget suy ra từ chính cửa sổ giờ làm việc đang cấu hình — khai số ngày ở một nơi duy nhất,
    // số giờ tính ra. Hard-code cả ngày lẫn giờ sẽ khiến nhãn "N ngày" lệch DueAt khi đổi cửa sổ.
    public int GetSlaMinutes(TicketPriorityEnum priority)
        => checked(GetSlaWorkingDays(priority) * _workingMinutesPerDay);

    public bool IsWorkingTime(DateTime instantUtc)
    {
        var local = ToLocal(instantUtc);
        return IsWorkingLocal(local);
    }

    public DateTime NormalizeToNextWorkingInstant(DateTime instantUtc)
    {
        var local = ToLocal(instantUtc);
        if (IsWorkingLocal(local))
            return ToUtc(local);

        var date = local.Date;
        if (IsWorkingDate(date) && local.TimeOfDay < _start)
            return ToUtc(date.Add(_start));

        do
        {
            date = date.AddDays(1);
        }
        while (!IsWorkingDate(date));

        return ToUtc(date.Add(_start));
    }

    public DateTime AddWorkingMinutes(DateTime startUtc, double minutes)
    {
        if (minutes < 0)
            throw new ArgumentOutOfRangeException(nameof(minutes), "Working minutes cannot be negative.");
        if (minutes == 0)
            return EnsureUtc(startUtc);

        var cursorUtc = NormalizeToNextWorkingInstant(startUtc);
        var remaining = minutes;
        const double epsilon = 0.0000001d;

        while (remaining > epsilon)
        {
            var local = ToLocal(cursorUtc);
            var closeUtc = ToUtc(local.Date.Add(_end));
            var available = Math.Max(0d, (closeUtc - cursorUtc).TotalMinutes);

            if (remaining <= available + epsilon)
                return EnsureUtc(cursorUtc.AddMinutes(remaining));

            remaining -= available;
            cursorUtc = NormalizeToNextWorkingInstant(closeUtc);
        }

        return EnsureUtc(cursorUtc);
    }

    public double GetWorkingMinutesBetween(DateTime startUtc, DateTime endUtc)
    {
        var start = EnsureUtc(startUtc);
        var end = EnsureUtc(endUtc);
        if (end <= start)
            return 0d;

        var firstDate = ToLocal(start).Date;
        var lastDate = ToLocal(end).Date;
        var total = 0d;

        for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
        {
            if (!IsWorkingDate(date))
                continue;

            var openUtc = ToUtc(date.Add(_start));
            var closeUtc = ToUtc(date.Add(_end));
            var segmentStart = start > openUtc ? start : openUtc;
            var segmentEnd = end < closeUtc ? end : closeUtc;
            if (segmentEnd > segmentStart)
                total += (segmentEnd - segmentStart).TotalMinutes;
        }

        // Time-zone round trips can introduce sub-tick floating-point noise. SLA accounting is
        // minute-based, so normalize microsecond-scale drift before callers truncate to minutes.
        return Math.Round(total, 7, MidpointRounding.AwayFromZero);
    }

    public double GetRemainingPercent(SlaTimer timer, DateTime atUtc)
    {
        if (timer.Status is not (SlaTimerStatusEnum.Running or SlaTimerStatusEnum.Paused))
            return 0d;

        var observedAt = timer.Status == SlaTimerStatusEnum.Paused && timer.CurrentPauseStartedAt.HasValue
            ? timer.CurrentPauseStartedAt.Value
            : atUtc;
        var remaining = GetWorkingMinutesBetween(observedAt, timer.DueAt);
        var budget = GetSlaMinutes(timer.Priority);
        var percent = Math.Clamp(remaining / budget * 100d, 0d, 100d);
        return Math.Round(percent, 2, MidpointRounding.AwayFromZero);
    }

    public bool ShouldSendNextSessionReminder(DateTime warningSentAtUtc, DateTime atUtc)
    {
        var warningLocal = ToLocal(warningSentAtUtc);
        if (!IsWorkingDate(warningLocal.Date)
            || warningLocal.TimeOfDay < _end - TimeSpan.FromHours(1)
            || warningLocal.TimeOfDay >= _end)
        {
            return false;
        }

        var nextOpeningUtc = NormalizeToNextWorkingInstant(ToUtc(warningLocal.Date.Add(_end)));
        var now = EnsureUtc(atUtc);
        return now >= nextOpeningUtc && IsWorkingTime(now);
    }

    private bool IsWorkingLocal(DateTime local) =>
        IsWorkingDate(local.Date)
        && local.TimeOfDay >= _start
        && local.TimeOfDay < _end;

    private bool IsWorkingDate(DateTime localDate) =>
        _workingDays.Contains(localDate.DayOfWeek)
        && !(_calendarProvider?.IsNonWorkingDate(DateOnly.FromDateTime(localDate)) ?? false);

    private DateTime ToLocal(DateTime instantUtc) =>
        TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(instantUtc), _timeZone);

    private DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _timeZone);

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
