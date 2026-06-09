using MediatR;
using TicketService.Application.DTOs.Response.Maintenance;

namespace TicketService.Application.CQRS.Query.MaintenanceLogs;

public class MaintenanceLogsByTicketQuery : IRequest<List<MaintenanceLogDTO>>
{
    public Guid TicketId { get; set; }

    public MaintenanceLogsByTicketQuery(Guid ticketId)
    {
        TicketId = ticketId;
    }
}
