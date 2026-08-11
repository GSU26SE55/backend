using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Implements.Utils;

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

    public DateTime CalculateDueDate(DateTime startedAt, TicketPriorityEnum priority)
        => startedAt.AddHours(GetSlaHours(priority));

    public int GetSlaHours(TicketPriorityEnum priority)
    {
        return priority switch
        {
            TicketPriorityEnum.P1Critical => 4,
            TicketPriorityEnum.P2High => 24,
            TicketPriorityEnum.P3Normal => 72,
            _ => throw new ArgumentOutOfRangeException(nameof(priority), $"Priority value {priority} is not supported for SLA calculation.")
        };
    }
}
