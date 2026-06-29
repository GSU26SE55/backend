namespace TicketService.Application.DTOs.Response.Reports;

/// <summary>Sprint 7 #114 (§5.2) — điểm time-series (date + count).</summary>
public class TicketTimeSeriesPoint
{
    public DateTime Date { get; set; }
    /// <summary>
    /// Count.
    /// </summary>
    public int Count { get; set; }
}
