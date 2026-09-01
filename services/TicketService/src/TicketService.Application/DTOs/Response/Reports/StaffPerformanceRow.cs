namespace TicketService.Application.DTOs.Response.Reports;

/// <summary>Sprint 7 #114 (§5.2) — hiệu suất staff.</summary>
public class StaffPerformanceRow
{
    public string StaffId { get; set; } = string.Empty;
    /// <summary>
    /// Name.
    /// </summary>
    public string? Name { get; set; }
    public int TicketsResolved { get; set; }
    public decimal AvgResolveHours { get; set; }
    /// <summary>
    /// Avg rating.
    /// </summary>
    public decimal? AvgRating { get; set; }
    public decimal SlaCompliance { get; set; }   // %
    public int RescueCount { get; set; }
    public int RescueSuccessCount { get; set; }
}
