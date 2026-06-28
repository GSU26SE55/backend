namespace TicketService.Application.DTOs.Response.Reports;

/// <summary>Sprint 7 #114 (§5.2) — hiệu suất staff.</summary>
public class StaffPerformanceRow
{
    public string StaffId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public int TicketsResolved { get; set; }
    public decimal AvgResolveHours { get; set; }
    public decimal? AvgRating { get; set; }
    public decimal SlaCompliance { get; set; }   // %
}
