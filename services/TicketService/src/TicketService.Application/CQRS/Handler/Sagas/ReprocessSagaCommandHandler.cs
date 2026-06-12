using MediatR;
using Microsoft.Extensions.Logging;
using SharedInfrastructure.Idempotency;
using SharedInfrastructure.Metrics;
using TicketService.Application.CQRS.Command.Sagas;
using TicketService.Application.DTOs.Response.Saga;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Application.CQRS.Handler.Sagas;

/// <summary>
/// Reset Saga ở Failed state về Initial → state machine pick up trên redelivery
/// của anomaly event (admin sẽ republish thủ công hoặc tự động qua reconciliation job).
///
/// Sprint 5B #239 (xem overall.md §53.11).
/// </summary>
public class ReprocessSagaCommandHandler : IRequestHandler<ReprocessSagaCommand, SagaReprocessResponse>
{
    private readonly IAlertTicketSagaQueryService _sagaQuery;
    private readonly IIdempotencyKeyStore _idempotency;
    private readonly ILogger<ReprocessSagaCommandHandler> _logger;

    public ReprocessSagaCommandHandler(
        IAlertTicketSagaQueryService sagaQuery,
        IIdempotencyKeyStore idempotency,
        ILogger<ReprocessSagaCommandHandler> logger)
    {
        _sagaQuery = sagaQuery;
        _idempotency = idempotency;
        _logger = logger;
    }

    public async Task<SagaReprocessResponse> Handle(ReprocessSagaCommand request, CancellationToken cancellationToken)
    {
        // Idempotency reservation — 24h window, key scoped per-AlertId.
        var idempotencyKey = $"saga.reprocess:{request.AlertId}:{request.IdempotencyKey}";
        var reserved = await _idempotency.TryReserveAsync(
            idempotencyKey, TimeSpan.FromHours(24), cancellationToken);

        if (!reserved)
        {
            _logger.LogInformation(
                "Saga reprocess duplicate request — AlertId={AlertId}, key={Key}",
                request.AlertId, request.IdempotencyKey);

            return new SagaReprocessResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Idempotent replay — previous reprocess accepted.",
                Data = new { alertId = request.AlertId, idempotent = true }
            };
        }

        var ok = await _sagaQuery.ResetFailedStateAsync(request.AlertId, cancellationToken);
        if (!ok)
        {
            return new SagaReprocessResponse
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = $"Saga cho AlertId {request.AlertId} không tồn tại hoặc không ở state Failed."
            };
        }

        AppMetrics.SagaReprocessed.Inc();

        _logger.LogInformation(
            "Saga reprocess accepted — AlertId={AlertId}, performedBy={UserId}",
            request.AlertId, request.PerformedBy);

        return new SagaReprocessResponse
        {
            IsSuccess = true,
            StatusCode = 202,
            Message = "Saga state reset to Initial. Republish anomaly event để tiếp tục.",
            Data = new { alertId = request.AlertId, performedBy = request.PerformedBy }
        };
    }
}
