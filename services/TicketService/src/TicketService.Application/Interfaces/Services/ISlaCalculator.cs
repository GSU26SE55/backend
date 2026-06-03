using TicketService.Domain.Entities;

namespace TicketService.Application.Interfaces.Services;

public interface ISlaCalculator
{
    DateTime CalculateSlaDueDate(Ticket ticket);
}
