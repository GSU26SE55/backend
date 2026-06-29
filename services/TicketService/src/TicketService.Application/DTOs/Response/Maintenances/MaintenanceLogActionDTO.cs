using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Maintenances;

public class MaintenanceLogActionDTO
{
    /// <summary>
    /// Id.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    public string? TicketId { get; set; }
    public string Code { get; set; } = string.Empty;
    /// <summary>
    /// Trạng thái.
    /// </summary>
    public TicketStatusEnum Status { get; set; }
}
