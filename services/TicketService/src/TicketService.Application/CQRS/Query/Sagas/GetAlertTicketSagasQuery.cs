using MediatR;
using TicketService.Application.DTOs.Response.Saga;

namespace TicketService.Application.CQRS.Query.Sagas;

/// <summary>
/// List/filter Alert–Ticket Saga states cho admin view.
///
/// Sprint 5B #239 — admin endpoint requires permission <c>ticket.saga.view</c>
/// (Admin + Manager read-only).
/// </summary>
public class GetAlertTicketSagasQuery : IRequest<AlertTicketSagaListResponse>
{
    /// <summary>Filter theo current state (vd "TicketRequested", "Failed", "Completed").</summary>
    public string? State { get; set; }

    public Guid? AlertId { get; set; }
    public Guid? BatteryAssetId { get; set; }
    public Guid? CustomerId { get; set; }

    public DateTime? StartedFrom { get; set; }
    public DateTime? StartedTo { get; set; }

    public bool? IsFailed { get; set; }

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public bool IsDescending { get; set; } = true;
}
