using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response;

public class MaintenanceLogDTO
{
    public string Id { get; set; } = string.Empty;
    public string TicketId { get; set; } = string.Empty;
    public string StaffId { get; set; } = string.Empty;
    public MaintenanceLogTypeEnum LogType { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? DiagnosisDetails { get; set; }
    public string? ActionsTaken { get; set; }
    public int DurationMinutes { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
