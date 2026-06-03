namespace BatteryService.Application.Services;

/// <summary>
/// Tìm alert Critical còn Open mà chưa được Ack quá N phút → publish (lại) qua Outbox
/// để TicketService/NotificationService escalate. Mỗi alert chỉ escalate 1 lần
/// (dedup theo Outbox.Type = "BatteryAnomalyEscalatedEvent").
/// </summary>
public interface IAlertEscalationService
{
    Task<AlertEscalationResult> EscalateAsync(
        int minutesUntilEscalate,
        int batchSize = 100,
        CancellationToken cancellationToken = default);
}

public class AlertEscalationResult
{
    public int Escalated { get; set; }
    public int AlreadyEscalated { get; set; }
}
