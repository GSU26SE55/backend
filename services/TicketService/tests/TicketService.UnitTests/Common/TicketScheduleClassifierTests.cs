using FluentAssertions;
using TicketService.Application.Common.Utils;

namespace TicketService.UnitTests.Common;

public class TicketScheduleClassifierTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 11, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Classify_OlderThanFiveMinuteWindow_IsInvalidPast()
    {
        var result = TicketScheduleClassifier.Classify(
            new DateTimeOffset(NowUtc.AddMinutes(-5).AddTicks(-1)), NowUtc, 5);

        result.Kind.Should().Be(ScheduleKind.InvalidPast);
    }

    [Fact]
    public void Classify_ExactlyAtFiveMinuteBoundary_IsCurrent()
    {
        var result = TicketScheduleClassifier.Classify(
            new DateTimeOffset(NowUtc.AddMinutes(-5)), NowUtc, 5);

        result.Kind.Should().Be(ScheduleKind.Current);
        result.ScheduledStartAtUtc.Should().Be(NowUtc);
    }

    [Fact]
    public void Classify_ExactlyNow_IsCurrent()
    {
        var result = TicketScheduleClassifier.Classify(new DateTimeOffset(NowUtc), NowUtc, 5);

        result.Kind.Should().Be(ScheduleKind.Current);
        result.ScheduledStartAtUtc.Should().Be(NowUtc);
    }

    [Fact]
    public void Classify_SmallestFutureInstant_IsFuture()
    {
        var requested = new DateTimeOffset(NowUtc.AddTicks(1));

        var result = TicketScheduleClassifier.Classify(requested, NowUtc, 5);

        result.Kind.Should().Be(ScheduleKind.Future);
        result.ScheduledStartAtUtc.Should().Be(requested.UtcDateTime);
    }

    [Fact]
    public void Classify_OffsetAwareValue_UsesEquivalentUtcInstant()
    {
        var requested = new DateTimeOffset(2026, 8, 11, 11, 30, 0, TimeSpan.FromHours(7));

        var result = TicketScheduleClassifier.Classify(requested, NowUtc, 5);

        result.Kind.Should().Be(ScheduleKind.Future);
        result.ScheduledStartAtUtc.Should().Be(new DateTime(2026, 8, 11, 4, 30, 0, DateTimeKind.Utc));
    }
}
