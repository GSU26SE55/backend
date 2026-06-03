using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.Services;

public class SlaCalculator : ISlaCalculator
{
    public DateTime CalculateSlaDueDate(Ticket ticket)
    {
        if (ticket.Priority == null)
        {
            throw new ArgumentNullException(nameof(ticket.Priority), "Ticket priority cannot be null to calculate SLA due date.");
        }

        var slaHours = GetSlaHours(ticket.Priority.Value);
        return ticket.CreatedAt.AddHours(slaHours);
    }

    private static int GetSlaHours(TicketPriorityEnum priority)
    {
        return priority switch
        {
            TicketPriorityEnum.P1Critical => 4,
            TicketPriorityEnum.P2High => 24,
            TicketPriorityEnum.P3Normal => 72,
            _ => throw new ArgumentOutOfRangeException(nameof(priority), $"Giá trị Priority {priority} không được hỗ trợ để tính SLA.")
        };
    }
}
