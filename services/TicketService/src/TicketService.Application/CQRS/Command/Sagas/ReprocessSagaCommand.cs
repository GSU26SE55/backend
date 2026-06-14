using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Saga;

namespace TicketService.Application.CQRS.Command.Sagas;

/// <summary>
/// Admin reprocess Saga đang ở Failed state.
///
/// Sprint 5B #239 — required permission <c>ticket.saga.reprocess</c> (Admin only).
/// Endpoint <c>POST /api/admin/sagas/alert-ticket/{alertId}/reprocess</c>
/// PHẢI có header <c>Idempotency-Key</c> (xem §8.6).
///
/// Mechanism: trigger re-republish anomaly event (V1/V2) qua MassTransit để Saga
/// pick up lại. Hoặc nếu state Failed do bound retry exhausted → reset retry counter
/// và resume từ <c>FailedAtStage</c>.
/// </summary>
public class ReprocessSagaCommand : IRequest<SagaReprocessResponse>, IValidatable<SagaReprocessResponse>
{
    [JsonIgnore]
    public Guid AlertId { get; set; }

    /// <summary>
    /// Idempotency-Key header value. Required.
    /// </summary>
    [JsonIgnore]
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// Admin actor performing reprocess (audit).
    /// </summary>
    [JsonIgnore]
    public Guid PerformedBy { get; set; }

    public Task<SagaReprocessResponse> ValidateAsync()
    {
        var response = new SagaReprocessResponse();

        if (AlertId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = nameof(AlertId), Detail = "AlertId không hợp lệ." });

        if (string.IsNullOrWhiteSpace(IdempotencyKey))
            response.ListErrors.Add(new Errors { Field = nameof(IdempotencyKey), Detail = "Idempotency-Key header bắt buộc." });

        if (PerformedBy == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = nameof(PerformedBy), Detail = "Admin user ID không xác định." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Reprocess request không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
