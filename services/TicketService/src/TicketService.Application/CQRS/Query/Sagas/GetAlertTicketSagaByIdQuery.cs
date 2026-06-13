using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using TicketService.Application.DTOs.Response.Saga;

namespace TicketService.Application.CQRS.Query.Sagas;

/// <summary>
/// Detail của 1 Saga state by AlertId (CorrelationId).
///
/// Sprint 5B #239 — required permission <c>ticket.saga.view</c>.
/// </summary>
public class GetAlertTicketSagaByIdQuery : IRequest<AlertTicketSagaDetailResponse>
{
    [JsonIgnore]
    [BindNever]
    public Guid AlertId { get; set; }
}
