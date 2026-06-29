namespace TicketService.Application.DTOs.Response.Reports;

/// <summary>Sprint 7 #114 (§5.2) — SLA compliance theo staff.</summary>
public class SlaByStaffRow
{
    public string StaffId { get; set; } = string.Empty;
    /// <summary>
    /// Name.
    /// </summary>
    public string? Name { get; set; }
    public int TotalAssigned { get; set; }
    public int Met { get; set; }
    /// <summary>
    /// Breached.
    /// </summary>
    public int Breached { get; set; }
    public decimal ComplianceRate { get; set; }   // %
}
