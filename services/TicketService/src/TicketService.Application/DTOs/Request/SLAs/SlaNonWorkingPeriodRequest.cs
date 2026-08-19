namespace TicketService.Application.DTOs.Request.SLAs;

public sealed class SlaNonWorkingPeriodRequest
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}
