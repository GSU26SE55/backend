using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedContracts.Events;
using SharedContracts.Events.Blog;
using SharedContracts.Events.Chats;
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
    private readonly IOutboxClaimService _claimService;
    private readonly IOutboxLeaseOwner _leaseOwner;
    private readonly IIntegrationEventTransport _transport;
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
        { nameof(TicketEscalatedEvent), typeof(TicketEscalatedEvent) },
        { nameof(SlaBreachedIntegrationEvent), typeof(SlaBreachedIntegrationEvent) },
        { nameof(SlaWarningIntegrationEvent), typeof(SlaWarningIntegrationEvent) },
        { nameof(IncidentDeclaredIntegrationEvent), typeof(IncidentDeclaredIntegrationEvent) },
        { nameof(TicketHeldIntegrationEvent), typeof(TicketHeldIntegrationEvent) },
        { nameof(TicketResumedIntegrationEvent), typeof(TicketResumedIntegrationEvent) },
        { nameof(TicketMergedEvent), typeof(TicketMergedEvent) },
        { nameof(BlogGenerationRequestedEvent), typeof(BlogGenerationRequestedEvent) },
        { nameof(BlogGenerationStatusChangedEvent), typeof(BlogGenerationStatusChangedEvent) },
        // Chat events are written by the TicketService chat handlers. Keep every
        // event here; otherwise the relay exhausts its retries with "Unknown event
        // type" and real chat notifications never reach NotificationService.
        { nameof(ChatCreatedEvent), typeof(ChatCreatedEvent) },
        { nameof(ChatMentionedEvent), typeof(ChatMentionedEvent) },
        { nameof(ChatReactedEvent), typeof(ChatReactedEvent) },
        { nameof(ChatEditedEvent), typeof(ChatEditedEvent) },
        { nameof(ChatDeletedEvent), typeof(ChatDeletedEvent) },
        { nameof(ChatEscalatedToAdminEvent), typeof(ChatEscalatedToAdminEvent) },
        { nameof(ChatEscalationReviewRequestedEvent), typeof(ChatEscalationReviewRequestedEvent) },
        { nameof(ChatEscalationReviewAckedEvent), typeof(ChatEscalationReviewAckedEvent) },
        { nameof(ParticipantAddedEvent), typeof(ParticipantAddedEvent) },
        { nameof(ParticipantRemovedEvent), typeof(ParticipantRemovedEvent) },
        { nameof(ParticipantRoleChangedEvent), typeof(ParticipantRoleChangedEvent) },
        { nameof(VoiceTranscriptionRequestedEvent), typeof(VoiceTranscriptionRequestedEvent) },
    };

    public OutboxRelayService(
        ITicketUnitOfWork unitOfWork,
        IOutboxClaimService claimService,
        IOutboxLeaseOwner leaseOwner,
        IIntegrationEventTransport transport,
        IOptions<OutboxOptions> options,
        ILogger<OutboxRelayService> logger)
    {
        _unitOfWork = unitOfWork;
        _claimService = claimService;
        _leaseOwner = leaseOwner;
        _transport = transport;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OutboxRelayResult> RelayBatchAsync(
        int batchSize = 100, CancellationToken cancellationToken = default)
    {
        var pendingIds = await _unitOfWork.OutboxMessages
            .GetAllAsync()
            .AsNoTracking()
            .Where(m => m.ProcessedAtUtc == null && m.RetryCount < _options.MaxRetryCount)
            .OrderBy(m => m.OccurredAtUtc)
            .Select(m => m.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var result = new OutboxRelayResult();
        if (pendingIds.Count == 0)
            return result;

        foreach (var messageId in pendingIds)
        {
            var msg = await _claimService.TryClaimAsync(
                messageId,
                _leaseOwner.Value,
                TimeSpan.FromSeconds(_options.LeaseDurationSeconds),
                cancellationToken);

            if (msg is null)
            {
                continue;
            }

            try
            {
                if (!EventTypeMap.TryGetValue(msg.Type, out var clrType))
                {
                    await _claimService.MarkFailedAsync(
                        msg.Id, _leaseOwner.Value, $"Unknown event type: {msg.Type}", cancellationToken);
                    result.Failed++;
                    continue;
                }

                var evt = (IntegrationEvent?)JsonSerializer.Deserialize(msg.Payload, clrType);
                if (evt is null)
                {
                    await _claimService.MarkFailedAsync(
                        msg.Id, _leaseOwner.Value, "Deserialize returned null", cancellationToken);
                    result.Failed++;
                    continue;
                }

                // Must invoke with runtime type so MassTransit routes to the correct exchange.
                // Calling PublishAsync(evt, ct) directly infers T = IntegrationEvent (base),
                // which would publish to the wrong exchange and consumers would never receive it.
                var publishMethod = typeof(IIntegrationEventTransport)
                    .GetMethod(nameof(IIntegrationEventTransport.PublishAsync))!
                    .MakeGenericMethod(clrType);

                using var publishTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                publishTimeout.CancelAfter(TimeSpan.FromSeconds(_options.PublishTimeoutSeconds));
                await (Task)publishMethod.Invoke(_transport, new object[] { evt, publishTimeout.Token })!;

                if (await _claimService.MarkProcessedAsync(msg.Id, _leaseOwner.Value, cancellationToken))
                {
                    result.Published++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                if (await _claimService.MarkFailedAsync(msg.Id, _leaseOwner.Value, error, cancellationToken))
                {
                    result.Failed++;
                }
                _logger.LogError(ex, "Failed to relay outbox message {Id}", msg.Id);
            }
        }
        return result;
    }
}
