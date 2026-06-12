using System.Text.Json;
using BatteryService.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;

namespace BatteryService.Application.Services;

public class OutboxRelayService : IOutboxRelayService
{
    /// <summary>
    /// Map type name (lưu trong cột <c>outbox_messages.type</c>) → CLR type cho deserialize.
    /// Mở rộng khi thêm event mới.
    /// </summary>
    private static readonly Dictionary<string, Type> EventTypeMap = new()
    {
        { nameof(BatteryAnomalyDetectedEvent), typeof(BatteryAnomalyDetectedEvent) },
        { nameof(BatteryAnomalyDetectedV2Event), typeof(BatteryAnomalyDetectedV2Event) },
        // Sprint 5B #238 — event mới cho NotificationService escalation flow.
        { nameof(BatteryAlertEscalationRequestedEvent), typeof(BatteryAlertEscalationRequestedEvent) },
        // Legacy alias — payload schema cũ (BatteryAnomalyDetectedEvent) cho rows
        // pre-Sprint-5B chưa relay. Deserialize an toàn vì BatteryAnomalyDetectedEvent
        // có sẵn handler ở TicketService (giờ ignored qua Saga tombstone).
        { "BatteryAnomalyEscalatedEvent", typeof(BatteryAnomalyDetectedEvent) }
    };

    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _producer;

    public OutboxRelayService(IBatteryUnitOfWork unitOfWork, IMessageProducerService producer)
    {
        _unitOfWork = unitOfWork;
        _producer = producer;
    }

    public async Task<OutboxRelayResult> RelayBatchAsync(
        int batchSize = 100, CancellationToken cancellationToken = default)
    {
        var pending = await _unitOfWork.OutboxMessages
            .GetAllAsync()
            .Where(m => m.ProcessedAtUtc == null)
            .OrderBy(m => m.OccurredAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var result = new OutboxRelayResult();
        if (pending.Count == 0)
            return result;

        foreach (var msg in pending)
        {
            try
            {
                if (!EventTypeMap.TryGetValue(msg.Type, out var clrType))
                {
                    msg.RetryCount += 1;
                    msg.LastError = $"Unknown event type: {msg.Type}";
                    _unitOfWork.OutboxMessages.UpdateAsync(msg);
                    result.Failed++;
                    continue;
                }

                var evt = (IntegrationEvent?)JsonSerializer.Deserialize(msg.Payload, clrType);
                if (evt is null)
                {
                    msg.RetryCount += 1;
                    msg.LastError = "Deserialize returned null";
                    _unitOfWork.OutboxMessages.UpdateAsync(msg);
                    result.Failed++;
                    continue;
                }

                await _producer.PublishAsync(evt, cancellationToken);
                msg.ProcessedAtUtc = DateTime.UtcNow;
                _unitOfWork.OutboxMessages.UpdateAsync(msg);
                result.Published++;
            }
            catch (Exception ex)
            {
                msg.RetryCount += 1;
                msg.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                _unitOfWork.OutboxMessages.UpdateAsync(msg);
                result.Failed++;
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }
}
