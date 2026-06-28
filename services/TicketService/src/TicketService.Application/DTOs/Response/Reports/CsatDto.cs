namespace TicketService.Application.DTOs.Response.Reports;

/// <summary>Sprint 7 #114 (§5.2) — chỉ số hài lòng khách hàng (CSAT).</summary>
public class CsatDto
{
    public decimal AvgRating { get; set; }
    public int TotalRated { get; set; }
    public Dictionary<int, int> RatingDistribution { get; set; } = new();   // 1..5 → count
}
