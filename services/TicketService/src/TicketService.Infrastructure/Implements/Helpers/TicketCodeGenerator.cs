using Microsoft.EntityFrameworkCore;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Infrastructure.Implements.Helpers;

public class TicketCodeGenerator : ITicketCodeGenerator
{
    private readonly ITicketUnitOfWork _uow;

    public TicketCodeGenerator(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<string> GenerateAsync()
    {
        var now = DateTime.UtcNow;
        var prefix = $"TKT-{now:yyMM}-";

        var lastTicket = await _uow.Tickets.GetAllAsync()
            .Where(t => t.Code.StartsWith(prefix))
            .OrderByDescending(t => t.Code)
            .FirstOrDefaultAsync();

        int nextNumber = 1;
        if (lastTicket != null && !string.IsNullOrEmpty(lastTicket.Code))
        {
            // Extract the NNNN part from TKT-YYMM-NNNN
            var parts = lastTicket.Code.Split('-');
            if (parts.Length == 3 && int.TryParse(parts[2], out int lastNumber))
            {
                nextNumber = lastNumber + 1;
            }
        }

        return $"{prefix}{nextNumber:D4}";
    }
}
