namespace TicketService.Application.Interfaces.Utils;

public interface ISlaBusinessCalendarProvider
{
    bool IsNonWorkingDate(DateOnly localDate);
    void Invalidate();
}
