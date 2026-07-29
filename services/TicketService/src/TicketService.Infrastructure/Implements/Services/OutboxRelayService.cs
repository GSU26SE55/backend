using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedContracts.Events.Blog;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Models;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

public class OutboxRelayService : IOutboxRelayService
{
    private readonly ITicketUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _producer;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxRelayService> _logger;

    // TODO: Map event types as they are created
    private static readonly Dictionary<string, Type> EventTypeMap = new()
    {
        // TicketCreatedEvent (SharedContracts) — dùng nội bộ cho TicketVerifyOnCreatedConsumer
        // (AI verify). PHẢI có ở đây, nếu không OutboxRelay bỏ qua → consumer không nhận → verify kẹt.
        { nameof(SharedContracts.Events.TicketCreatedEvent), typeof(SharedContracts.Events.TicketCreatedEvent) },
        { nameof(TicketCreatedIntegrationEvent), typeof(TicketCreatedIntegrationEvent) },
        { nameof(TicketAssignedIntegrationEvent), typeof(TicketAssignedIntegrationEvent) },
        { nameof(TicketResolvedIntegrationEvent), typeof(TicketResolvedIntegrationEvent) },
        { nameof(TicketApprovedIntegrationEvent), typeof(TicketApprovedIntegrationEvent) },
        { nameof(TicketRejectedIntegrationEvent), typeof(TicketRejectedIntegrationEvent) },
        { nameof(TicketStatusChangedIntegrationEvent), typeof(TicketStatusChangedIntegrationEvent) },
        { nameof(TicketReopenedIntegrationEvent), typeof(TicketReopenedIntegrationEvent) },
        { nameof(TicketRatedIntegrationEvent), typeof(TicketRatedIntegrationEvent) },
        { nameof(TicketEscalatedIntegrationEvent), typeof(TicketEscalatedIntegrationEvent) },
        { nameof(SlaBreachedIntegrationEvent), typeof(SlaBreachedIntegrationEvent) },
        { nameof(SlaWarningIntegrationEvent), typeof(SlaWarningIntegrationEvent) },
        { nameof(IncidentDeclaredIntegrationEvent), typeof(IncidentDeclaredIntegrationEvent) },
        { nameof(TicketHeldIntegrationEvent), typeof(TicketHeldIntegrationEvent) },
        { nameof(TicketResumedIntegrationEvent), typeof(TicketResumedIntegrationEvent) },
        { nameof(BlogGenerationRequestedEvent), typeof(BlogGenerationRequestedEvent) },
        { nameof(BlogGenerationStatusChangedEvent), typeof(BlogGenerationStatusChangedEvent) },
    };

    public OutboxRelayService(
        ITicketUnitOfWork unitOfWork,
        IMessageProducerService producer,
        IOptions<OutboxOptions> options,
        ILogger<OutboxRelayService> logger)
    {
        _unitOfWork = unitOfWork;
        _producer = producer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OutboxRelayResult> RelayBatchAsync(
        int batchSize = 100, CancellationToken cancellationToken = default)
    {
        var pending = await _unitOfWork.OutboxMessages
            .GetAllAsync()
            .Where(m => m.ProcessedAtUtc == null && m.RetryCount < _options.MaxRetryCount)
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

                // Must invoke with runtime type so MassTransit routes to the correct exchange.
                // Calling PublishAsync(evt, ct) directly infers T = IntegrationEvent (base),
                // which would publish to the wrong exchange and consumers would never receive it.
                var publishMethod = typeof(IMessageProducerService)
                    .GetMethod(nameof(IMessageProducerService.PublishAsync))!
                    .MakeGenericMethod(clrType);
                await (Task)publishMethod.Invoke(_producer, new object[] { evt, cancellationToken })!;
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
                _logger.LogError(ex, "Failed to relay outbox message {Id}", msg.Id);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }
}
