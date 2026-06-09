using MediatR;
using TicketService.Application.DTOs.Response.Maintenance;

namespace TicketService.Application.CQRS.Query.MaintenanceLogs;

public class MyMaintenanceLogsQuery : IRequest<List<StaffMaintenanceLogGroupDTO>>
{
    public Guid StaffId { get; set; }

    public MyMaintenanceLogsQuery(Guid staffId)
    {
        StaffId = staffId;
    }
}
