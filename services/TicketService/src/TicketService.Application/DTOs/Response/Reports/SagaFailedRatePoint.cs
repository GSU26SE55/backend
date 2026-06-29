namespace TicketService.Application.DTOs.Response.Reports;

/// <summary>Sprint 7 #114 (§5.2) — saga failed rate theo thời gian.</summary>
public class SagaFailedRatePoint
{
    public DateTime Date { get; set; }
    /// <summary>
    /// Started.
    /// </summary>
    public int Started { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
    /// <summary>
    /// Failed rate.
    /// </summary>
    public decimal FailedRate { get; set; }       // %
    public decimal P95DurationSec { get; set; }
}
