using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketService.Infrastructure.Persistence;

namespace TicketService.Infrastructure.BackgroundJobs;

/// <summary>
/// Daily 03:00 UTC: soft-delete (IsDeleted=true) chats older than Chat:Retention:ArchiveAfterYears.
/// KHÔNG xóa row — set IsDeleted để exclude khỏi query, giữ audit trail.
/// #569 GDPR retention policy.
/// </summary>
public class ChatRetentionService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ChatRetentionService> _logger;
    private readonly int _archiveAfterYears;

    public ChatRetentionService(
        IServiceProvider serviceProvider,
        ILogger<ChatRetentionService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _archiveAfterYears = configuration.GetValue("Chat:Retention:ArchiveAfterYears", 2);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeDelayToNextRun();
            _logger.LogInformation("[ChatRetention] Next run in {Delay:hh\\:mm}", delay);
            await Task.Delay(delay, stoppingToken);

            await RunRetentionAsync(stoppingToken);
        }
    }

    private async Task RunRetentionAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();

            var cutoff = DateTime.UtcNow.AddYears(-_archiveAfterYears);
            var chats = await db.TicketChats
                .Where(c => !c.IsDeleted && c.CreatedAt < cutoff)
                .ToListAsync(ct);

            if (chats.Count == 0)
            {
                _logger.LogInformation("[ChatRetention] No chats to archive.");
                return;
            }

            var now = DateTime.UtcNow;
            foreach (var chat in chats)
            {
                chat.IsDeleted = true;
                chat.DeletedAt = now;
            }

            await db.SaveChangesAsync(ct);
            _logger.LogInformation("[ChatRetention] Archived {Count} chats older than {Years} years.", chats.Count, _archiveAfterYears);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatRetention] Retention job failed.");
        }
    }

    private static TimeSpan ComputeDelayToNextRun()
    {
        var now = DateTime.UtcNow;
        var next = new DateTime(now.Year, now.Month, now.Day, 3, 0, 0, DateTimeKind.Utc);
        if (next <= now)
            next = next.AddDays(1);
        return next - now;
    }
}
