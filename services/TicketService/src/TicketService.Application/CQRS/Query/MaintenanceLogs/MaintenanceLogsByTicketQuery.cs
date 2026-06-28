using MediatR;
using TicketService.Application.DTOs.Response.Maintenances;

namespace TicketService.Application.CQRS.Query.MaintenanceLogs;

public class MaintenanceLogsByTicketQuery : IRequest<List<MaintenanceLogDTO>>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    public Guid TicketId { get; set; }

    public MaintenanceLogsByTicketQuery(Guid ticketId)
    {
        TicketId = ticketId;
    }
}
