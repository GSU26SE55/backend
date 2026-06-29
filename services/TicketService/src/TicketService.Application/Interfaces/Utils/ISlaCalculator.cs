using TicketService.Domain.Entities;

namespace TicketService.Application.Interfaces.Utils;

public interface ISlaCalculator
{
    DateTime CalculateSlaDueDate(Ticket ticket);
}
