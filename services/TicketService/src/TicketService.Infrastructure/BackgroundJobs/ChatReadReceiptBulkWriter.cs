using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.BackgroundJobs;

/// <summary>
/// Drain <see cref="IChatReadReceiptQueue"/> theo batch 100 record hoặc mỗi 1s (cái nào đến trước)
/// để giảm DB pressure khi nhiều user mark-read đồng thời (#542).
/// </summary>
public class ChatReadReceiptBulkWriter : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    private readonly IChatReadReceiptQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ChatReadReceiptBulkWriter> _logger;

    public ChatReadReceiptBulkWriter(
        IChatReadReceiptQueue queue,
        IServiceProvider serviceProvider,
        ILogger<ChatReadReceiptBulkWriter> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _queue.Reader;
        var buffer = new List<ChatReadReceiptItem>(BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            var deadline = DateTime.UtcNow + FlushInterval;

            while (buffer.Count < BatchSize)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    break;

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeoutCts.CancelAfter(remaining);

                bool canRead;
                try
                {
                    canRead = await reader.WaitToReadAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    break; // hết 1s deadline — flush phần đã có
                }

                if (!canRead)
                    break; // channel completed (shutdown)

                while (buffer.Count < BatchSize && reader.TryRead(out var item))
                    buffer.Add(item);
            }

            if (buffer.Count > 0)
            {
                await FlushAsync(buffer, stoppingToken);
                buffer.Clear();
            }
        }
    }

    private async Task FlushAsync(List<ChatReadReceiptItem> batch, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITicketUnitOfWork>();

        try
        {
            var deduped = batch
                .GroupBy(i => (i.ChatId, i.UserId))
                .Select(g => g.First())
                .ToList();

            var chatIds = deduped.Select(i => i.ChatId).Distinct().ToList();

            var existingPairs = await uow.TicketChatReads.GetAllAsync()
                .Where(r => chatIds.Contains(r.ChatId))
                .Select(r => new { r.ChatId, r.UserId })
                .ToListAsync(ct);

            var existingSet = existingPairs.Select(p => (p.ChatId, p.UserId)).ToHashSet();

            var chats = await uow.TicketChats.GetAllAsync()
                .Where(c => chatIds.Contains(c.Id) && !c.IsDeleted)
                .ToDictionaryAsync(c => c.Id, ct);

            foreach (var item in deduped)
            {
                if (existingSet.Contains((item.ChatId, item.UserId)))
                    continue;

                if (!chats.TryGetValue(item.ChatId, out var chat))
                    continue; // chat bị xóa giữa lúc enqueue và flush — bỏ qua

                await uow.TicketChatReads.AddAsync(new TicketChatRead
                {
                    Id = Guid.NewGuid(),
                    ChatId = item.ChatId,
                    Chat = chat,
                    UserId = item.UserId,
                    UserRole = item.UserRole,
                    ReadAt = item.ReadAt
                });
            }

            await uow.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ChatReadReceiptBulkWriter failed to flush {Count} read receipts.", batch.Count);
        }
    }
}
