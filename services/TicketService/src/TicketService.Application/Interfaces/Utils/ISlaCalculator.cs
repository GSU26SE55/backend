using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.Interfaces.Utils;

public interface ISlaCalculator
{
    DateTime CalculateSlaDueDate(Ticket ticket);

    /// <summary>Số ngày làm việc SLA theo priority (P1=1 ngày · P2=3 ngày · P3=7 ngày).</summary>
    int GetSlaWorkingDays(TicketPriorityEnum priority);

    /// <summary>Số giờ làm việc SLA theo priority — suy ra từ số ngày (P1=10h · P2=30h · P3=70h).</summary>
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
