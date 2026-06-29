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
    /// <summary>
    /// ID của thiết bị pin.
    /// </summary>
    public Guid? BatteryAssetId { get; set; }
    public Guid? CustomerId { get; set; }

    /// <summary>
    /// Started from.
    /// </summary>
    public DateTime? StartedFrom { get; set; }
    public DateTime? StartedTo { get; set; }

    /// <summary>
    /// Is failed.
    /// </summary>
    public bool? IsFailed { get; set; }

    public int PageNumber { get; set; } = 1;
    /// <summary>
    /// Kích thước trang (số lượng bản ghi trên một trang).
    /// </summary>
    public int PageSize { get; set; } = 50;
    public bool IsDescending { get; set; } = true;
}
