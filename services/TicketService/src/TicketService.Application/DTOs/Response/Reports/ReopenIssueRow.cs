namespace TicketService.Application.DTOs.Response.Reports;

/// <summary>Sprint 7 #114 (§5.2) — top issue bị reopen theo category.</summary>
public class ReopenIssueRow
{
    public string Category { get; set; } = string.Empty;
    /// <summary>
    /// Count.
    /// </summary>
    public int Count { get; set; }              // số ticket bị reopen (ReopenCount > 0)
    public decimal AvgReopenCount { get; set; }
}
