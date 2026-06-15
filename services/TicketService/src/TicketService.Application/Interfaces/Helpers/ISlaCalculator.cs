using TicketService.Domain.Entities;

namespace TicketService.Application.Interfaces.Helpers;

public interface ISlaCalculator
{
    DateTime CalculateSlaDueDate(Ticket ticket);
}
