namespace TicketService.Application.DTOs.Response.Reports;

/// <summary>Sprint 7 #114 (§5.2) — SLA compliance theo priority.</summary>
public class SlaByPriorityRow
{
    public string Priority { get; set; } = string.Empty;
    public int Met { get; set; }
    public int Breached { get; set; }
    public int Total { get; set; }
    public decimal ComplianceRate { get; set; }
}
