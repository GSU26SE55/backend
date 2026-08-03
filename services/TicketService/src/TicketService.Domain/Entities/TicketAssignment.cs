using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class TicketAssignment : AuditableEntity
{
    public Guid TicketId { get; set; }
    public Guid StaffId { get; set; }
    public AssignmentRoleEnum Role { get; set; }

    public Ticket? Ticket { get; set; }
}
