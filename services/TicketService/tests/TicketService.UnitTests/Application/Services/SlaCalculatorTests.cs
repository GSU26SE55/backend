using Microsoft.Extensions.Options;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Utils;

namespace TicketService.UnitTests.Application.Services;

public class SlaCalculatorTests
{
    private readonly SlaCalculator _sut = new();

    [Theory]
    [InlineData(TicketPriorityEnum.P1Critical, 240)]
    [InlineData(TicketPriorityEnum.P2High, 1440)]
    [InlineData(TicketPriorityEnum.P3Normal, 4320)]
    public void GetSlaMinutes_ShouldReturnWorkingMinuteBudget(
        TicketPriorityEnum priority,
        int expectedMinutes)
    {
        _sut.GetSlaMinutes(priority).Should().Be(expectedMinutes);
    }

    [Theory]
    [InlineData(23, 59, false)] // 06:59 local on the following day
    [InlineData(0, 0, true)]    // 07:00 local
    [InlineData(9, 59, true)]   // 16:59 local
    [InlineData(10, 0, false)]  // 17:00 local
    public void IsWorkingTime_ShouldHonorExactBoundaries(int utcHour, int utcMinute, bool expected)
    {
        var mondayUtc = new DateTime(2026, 8, 17, utcHour, utcMinute, 0, DateTimeKind.Utc);

        _sut.IsWorkingTime(mondayUtc).Should().Be(expected);
    }

    [Fact]
    public void NormalizeToNextWorkingInstant_ShouldMoveBeforeOpeningToSameDayOpening()
    {
        var monday0600Local = Utc(2026, 8, 16, 23);

        _sut.NormalizeToNextWorkingInstant(monday0600Local)
            .Should().Be(Utc(2026, 8, 17, 0));
    }

    [Fact]
    public void NormalizeToNextWorkingInstant_ShouldMoveFridayCloseToSaturdayOpening()
    {
        var friday1700Local = Utc(2026, 8, 21, 10);

        _sut.NormalizeToNextWorkingInstant(friday1700Local)
            .Should().Be(Utc(2026, 8, 22, 0));
    }

    [Fact]
    public void CalculateDueDate_P1StartingFridayAt1600_ShouldBeSaturdayAt1000()
    {
        var friday1600Local = Utc(2026, 8, 21, 9);

        _sut.CalculateDueDate(friday1600Local, TicketPriorityEnum.P1Critical)
            .Should().Be(Utc(2026, 8, 22, 3));
    }

    [Fact]
    public void CalculateDueDate_P2StartingMondayAt0700_ShouldBeWednesdayAt1100()
    {
        var monday0700Local = Utc(2026, 8, 17, 0);

        _sut.CalculateDueDate(monday0700Local, TicketPriorityEnum.P2High)
            .Should().Be(Utc(2026, 8, 19, 4));
    }

    [Fact]
    public void CalculateDueDate_P3_ShouldConsumeExactly4320WorkingMinutes()
    {
        var monday0700Local = Utc(2026, 8, 17, 0);
        var dueAt = _sut.CalculateDueDate(monday0700Local, TicketPriorityEnum.P3Normal);

        dueAt.Should().Be(Utc(2026, 8, 24, 2));
        _sut.GetWorkingMinutesBetween(monday0700Local, dueAt).Should().Be(4320);
    }

    [Fact]
    public void GetWorkingMinutesBetween_ShouldCountWeekendAndSkipNights()
    {
        var friday1600Local = Utc(2026, 8, 21, 9);
        var monday1000Local = Utc(2026, 8, 24, 3);

        _sut.GetWorkingMinutesBetween(friday1600Local, monday1000Local)
            .Should().Be(1440);
    }

    [Fact]
    public void GetWorkingMinutesBetween_ShouldNormalizeSubTickFloatingPointDrift()
    {
        var startedAt = Utc(2026, 8, 17, 2).AddTicks(1);
        var dueAt = _sut.CalculateDueDate(startedAt, TicketPriorityEnum.P1Critical);

        _sut.GetWorkingMinutesBetween(startedAt, dueAt).Should().Be(240);
    }

    [Fact]
    public void GetRemainingPercent_ShouldFreezeOutsideBusinessHours()
    {
        var timer = new SlaTimer
        {
            Priority = TicketPriorityEnum.P1Critical,
            Status = SlaTimerStatusEnum.Running,
            StartedAt = Utc(2026, 8, 21, 9),
            DueAt = Utc(2026, 8, 22, 3)
        };

        var fridayClose = _sut.GetRemainingPercent(timer, Utc(2026, 8, 21, 10));
        var saturdayBeforeOpening = _sut.GetRemainingPercent(timer, Utc(2026, 8, 21, 23, 59));

        fridayClose.Should().Be(75);
        saturdayBeforeOpening.Should().Be(75);
    }

    [Fact]
    public void ShouldSendNextSessionReminder_ShouldOnlyMatchLateDayWarningOnceNextSessionStarts()
    {
        var friday1630Local = Utc(2026, 8, 21, 9, 30);

        _sut.ShouldSendNextSessionReminder(friday1630Local, Utc(2026, 8, 21, 23, 59))
            .Should().BeFalse();
        _sut.ShouldSendNextSessionReminder(friday1630Local, Utc(2026, 8, 22, 0))
            .Should().BeTrue();
        _sut.ShouldSendNextSessionReminder(Utc(2026, 8, 22, 0), Utc(2026, 8, 23, 0))
            .Should().BeFalse();
    }

    [Fact]
    public void DeclaredNonWorkingDate_ShouldBeSkippedAndResumeNextDayAtOpening()
    {
        var calendar = new Mock<ISlaBusinessCalendarProvider>();
        calendar.Setup(x => x.IsNonWorkingDate(new DateOnly(2026, 8, 22))).Returns(true);
        var calculator = new SlaCalculator(Options.Create(new SlaBusinessHoursOptions()), calendar.Object);

        calculator.NormalizeToNextWorkingInstant(Utc(2026, 8, 22, 0))
            .Should().Be(Utc(2026, 8, 23, 0));
        calculator.CalculateDueDate(Utc(2026, 8, 21, 9), TicketPriorityEnum.P1Critical)
            .Should().Be(Utc(2026, 8, 23, 3));
    }

    [Theory]
    [InlineData((TicketPriorityEnum)0)]
    [InlineData((TicketPriorityEnum)99)]
    [InlineData((TicketPriorityEnum)(-1))]
    public void CalculateSlaDueDate_ShouldThrow_WhenPriorityIsInvalid(TicketPriorityEnum invalidPriority)
    {
        var ticket = ValidTicket(invalidPriority);

        var act = () => _sut.CalculateSlaDueDate(ticket);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage($"*Priority value {invalidPriority} is not supported*");
    }

    [Fact]
    public void CalculateSlaDueDate_ShouldThrow_WhenPriorityIsNull()
    {
        var ticket = ValidTicket(null);

        var act = () => _sut.CalculateSlaDueDate(ticket);

        act.Should().Throw<ArgumentNullException>();
    }

    private static Ticket ValidTicket(TicketPriorityEnum? priority) => new()
    {
        CreatedAt = Utc(2026, 8, 17, 1),
        Priority = priority,
        Code = "T1",
        Title = "Test",
        Description = "Test",
        Category = TicketCategoryEnum.Other,
        Status = TicketStatusEnum.Open,
        Origin = TicketOriginEnum.ManualByCustomer
    };

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
