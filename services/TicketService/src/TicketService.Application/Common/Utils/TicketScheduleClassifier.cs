namespace TicketService.Application.Common.Utils;

public static class TicketScheduleClassifier
{
    public static ScheduleClassification Classify(DateTimeOffset requested, DateTime nowUtc, int currentWindowMinutes)
    {
        var scheduledUtc = requested.UtcDateTime;
        if (scheduledUtc < nowUtc.AddMinutes(-currentWindowMinutes))
            return new(ScheduleKind.InvalidPast, scheduledUtc);

        return scheduledUtc <= nowUtc
            ? new(ScheduleKind.Current, nowUtc)
            : new(ScheduleKind.Future, scheduledUtc);
    }
}

public enum ScheduleKind { InvalidPast, Current, Future }
public readonly record struct ScheduleClassification(ScheduleKind Kind, DateTime ScheduledStartAtUtc);
