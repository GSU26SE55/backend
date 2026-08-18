using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.Interfaces.Utils;

public interface ISlaCalculator
{
    DateTime CalculateSlaDueDate(Ticket ticket);

    /// <summary>Số giờ làm việc SLA theo priority (P1=4h · P2=24h · P3=72h).</summary>
    int GetSlaHours(TicketPriorityEnum priority);

    int GetSlaMinutes(TicketPriorityEnum priority);

    bool IsWorkingTime(DateTime instantUtc);

    DateTime NormalizeToNextWorkingInstant(DateTime instantUtc);

    DateTime AddWorkingMinutes(DateTime startUtc, double minutes);

    double GetWorkingMinutesBetween(DateTime startUtc, DateTime endUtc);

    double GetRemainingPercent(SlaTimer timer, DateTime atUtc);

    bool ShouldSendNextSessionReminder(DateTime warningSentAtUtc, DateTime atUtc);

    DateTime CalculateDueDate(DateTime startedAt, TicketPriorityEnum priority);
}
