namespace TicketService.Application.DTOs.Response.Reports;

/// <summary>Sprint 7 #114 (§5.2) — phân bố ticket theo category.</summary>
public class CategoryBreakdownRow
{
    public string Category { get; set; } = string.Empty;
    /// <summary>
    /// Count.
    /// </summary>
    public int Count { get; set; }
}
