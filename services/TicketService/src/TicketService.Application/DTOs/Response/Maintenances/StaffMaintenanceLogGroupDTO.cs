namespace TicketService.Application.DTOs.Response.Maintenances;

public class StaffMaintenanceLogGroupDTO
{
    public string TicketId { get; set; } = string.Empty;
    public string TicketCode { get; set; } = string.Empty;
    public string TicketTitle { get; set; } = string.Empty;
    public List<MaintenanceLogDTO> Logs { get; set; } = new();
}
