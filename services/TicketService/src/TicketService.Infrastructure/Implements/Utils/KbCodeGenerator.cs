using Microsoft.EntityFrameworkCore;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;

namespace TicketService.Infrastructure.Implements.Utils;

public class KbCodeGenerator : IKbCodeGenerator
{
    private readonly ITicketUnitOfWork _uow;

    public KbCodeGenerator(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<string> GenerateNextCodeAsync(CancellationToken ct = default)
    {
        var currentYear = DateTime.UtcNow.Year;
        var prefix = $"KB-{currentYear}-";

        var lastArticle = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .Where(a => a.Code.StartsWith(prefix))
            .OrderByDescending(a => a.Code)
            .FirstOrDefaultAsync(ct);

        int nextNumber = 1;
        if (lastArticle != null)
        {
            var parts = lastArticle.Code.Split('-');
            if (parts.Length == 3 && int.TryParse(parts[2], out int lastNumber))
            {
                nextNumber = lastNumber + 1;
            }
        }

        return $"{prefix}{nextNumber:D4}";
    }
}
