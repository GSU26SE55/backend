namespace TicketService.Application.DTOs.Response.Saga;

public class AlertTicketSagaDTO
{
    /// <summary>
    /// Correlation id.
    /// </summary>
    public string CorrelationId { get; set; } = string.Empty;
    public string CurrentState { get; set; } = string.Empty;
    public string AlertId { get; set; } = string.Empty;
    /// <summary>
    /// ID của thiết bị pin.
    /// </summary>
    public string? BatteryAssetId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string? AssetSerialNumber { get; set; }
    /// <summary>
    /// Anomaly type.
    /// </summary>
    public int AnomalyType { get; set; }
    public int Severity { get; set; }
    public string? TicketId { get; set; }
    /// <summary>
    /// Ticket code.
    /// </summary>
    public string? TicketCode { get; set; }
    public bool TicketIsReused { get; set; }
    public string? FailedAtStage { get; set; }
    /// <summary>
    /// Failure reason.
    /// </summary>
    public string? FailureReason { get; set; }
    public string? FailureErrorCode { get; set; }
    public DateTime? FailedAt { get; set; }
    /// <summary>
    /// Retry count.
    /// </summary>
    public int RetryCount { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
