namespace TicketService.Application.DTOs.Response.Maintenances;

public class StaffMaintenanceLogGroupDTO
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    public string TicketId { get; set; } = string.Empty;
    public string TicketCode { get; set; } = string.Empty;
    public string TicketTitle { get; set; } = string.Empty;
    /// <summary>
    /// Logs.
    /// </summary>
    public List<MaintenanceLogDTO> Logs { get; set; } = new();
}
