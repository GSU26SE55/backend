namespace TicketService.Application.DTOs.Response.Saga;

public class AlertTicketSagaDTO
{
    public string CorrelationId { get; set; } = string.Empty;
    public string CurrentState { get; set; } = string.Empty;
    public string AlertId { get; set; } = string.Empty;
    public string? BatteryAssetId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string? AssetSerialNumber { get; set; }
    public int AnomalyType { get; set; }
    public int Severity { get; set; }
    public string? TicketId { get; set; }
    public string? TicketCode { get; set; }
    public bool TicketIsReused { get; set; }
    public string? FailedAtStage { get; set; }
    public string? FailureReason { get; set; }
    public string? FailureErrorCode { get; set; }
    public DateTime? FailedAt { get; set; }
    public int RetryCount { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
