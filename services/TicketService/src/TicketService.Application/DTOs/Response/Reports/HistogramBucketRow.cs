namespace TicketService.Application.DTOs.Response.Reports;

/// <summary>Sprint 7 #114 (§5.2) — 1 bucket của histogram resolution time.</summary>
public class HistogramBucketRow
{
    public string Bucket { get; set; } = string.Empty;   // vd "0-1h", "1-4h"
    /// <summary>
    /// Count.
    /// </summary>
    public int Count { get; set; }
}
