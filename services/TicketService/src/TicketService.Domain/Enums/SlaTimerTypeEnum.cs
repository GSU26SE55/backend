namespace TicketService.Domain.Enums;

public enum SlaTimerTypeEnum
{
    Response = 1,   // Stage 1: calendar hours, Open status
    Resolution = 2,   // Stage 2: working hours, InProgress status
}
