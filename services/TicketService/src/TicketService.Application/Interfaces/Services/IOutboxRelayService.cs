namespace TicketService.Application.Interfaces.Services;

/// <summary>
/// Đọc batch <c>outbox_messages</c> chưa processed → publish lên RabbitMQ → mark processed.
/// Background-only — chạy bởi OutboxRelayBackgroundService.
/// </summary>
public interface IOutboxRelayService
{
    Task<OutboxRelayResult> RelayBatchAsync(
        int batchSize = 100,
        CancellationToken cancellationToken = default);
}

public class OutboxRelayResult
{
    public int Published { get; set; }
    public int Failed { get; set; }
}
