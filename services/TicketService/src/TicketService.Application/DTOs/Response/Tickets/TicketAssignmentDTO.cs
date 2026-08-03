using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Tickets;

public class TicketAssignmentDTO
{
    public string StaffId { get; set; } = string.Empty;
    public AssignmentRoleEnum Role { get; set; }
}
