using TicketService.Infrastructure.Persistence;

namespace TicketService.Infrastructure.Persistence.Seeders;

public class TicketDataSeeder
{
    private readonly TicketDbContext _context;

    public TicketDataSeeder(TicketDbContext context)
    {
        _context = context;
    }

    public async Task SeedAsync()
    {
        // Add seeding logic here when needed for Sprint 4 foundation
        await Task.CompletedTask;
    }
}
