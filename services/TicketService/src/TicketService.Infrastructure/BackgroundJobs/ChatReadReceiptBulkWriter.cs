using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

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

            // Tracking (không AsNoTracking): row soft-delete phải REVIVE tại chỗ chứ không insert
            // mới — unique index ix_ticket_chat_reads_chat_user KHÔNG có filter IsDeleted nên insert
            // trùng (chat_id, user_id) sẽ vi phạm constraint và làm hỏng cả batch.
            var existingRows = await uow.TicketChatReads.GetAllAsync()
                .Where(r => chatIds.Contains(r.ChatId))
                .ToListAsync(ct);

            var existingByKey = existingRows.ToDictionary(r => (r.ChatId, r.UserId));

            var chats = await uow.TicketChats.GetAllAsync()
                .Where(c => chatIds.Contains(c.Id) && !c.IsDeleted)
                .ToDictionaryAsync(c => c.Id, ct);

            // Chỉ receipt THỰC SỰ mới được ghi mới bắn realtime — ghi trùng (đã đọc rồi) mà vẫn
            // bắn thì người gửi nhận sự kiện "đã xem" lặp lại mỗi lần đối phương mở lại chat.
            var persisted = new List<ChatReadReceiptItem>();

            foreach (var item in deduped)
            {
                if (existingByKey.TryGetValue((item.ChatId, item.UserId), out var existing))
                {
                    if (!existing.IsDeleted)
                        continue; // đã đọc rồi — giữ nguyên mốc ReadAt đầu tiên

                    existing.IsDeleted = false;
                    existing.DeletedAt = null;
                    existing.ReadAt = item.ReadAt;
                    existing.UserRole = item.UserRole;
                    existing.UpdatedAt = item.ReadAt;
                    uow.TicketChatReads.UpdateAsync(existing);
                    persisted.Add(item);
                    continue;
                }

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
                persisted.Add(item);
            }

            await uow.SaveChangesAsync(ct);

            await NotifyReadAsync(scope, uow, persisted, chats, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ChatReadReceiptBulkWriter failed to flush {Count} read receipts.", batch.Count);
        }
    }

    /// <summary>
    /// Bắn "ChatRead" tới TÁC GIẢ của từng tin vừa được đọc (tick "đã xem" kiểu Messenger).
    /// Lỗi realtime KHÔNG được làm hỏng flush: receipt đã nằm trong DB rồi, client refetch vẫn thấy.
    /// </summary>
    private async Task NotifyReadAsync(
        IServiceScope scope,
        ITicketUnitOfWork uow,
        List<ChatReadReceiptItem> persisted,
        Dictionary<Guid, TicketChat> chats,
        CancellationToken ct)
    {
        if (persisted.Count == 0)
            return;

        try
        {
            var notifier = scope.ServiceProvider.GetService<ITicketChatRealtimeNotifier>();
            if (notifier is null)
                return;

            var readerIds = persisted.Select(p => p.UserId).Distinct().ToList();

            var customers = await uow.CustomerAccounts.GetAllAsync().AsNoTracking()
                .Where(a => readerIds.Contains(a.AccountId) && !a.IsDeleted)
                .Select(a => new { a.AccountId, a.FullName, a.AvatarUrl })
                .ToListAsync(ct);
            var staffs = await uow.StaffAccounts.GetAllAsync().AsNoTracking()
                .Where(a => readerIds.Contains(a.AccountId) && !a.IsDeleted)
                .Select(a => new { a.AccountId, a.FullName, a.AvatarUrl })
                .ToListAsync(ct);

            var customerById = customers.ToDictionary(a => a.AccountId);
            var staffById = staffs.ToDictionary(a => a.AccountId);

            // Gom theo (ticket, tác giả) — mỗi tác giả nhận 1 sự kiện cho mỗi ticket.
            var byTicketAndAuthor = new Dictionary<Guid, Dictionary<Guid, List<ChatReaderDTO>>>();

            foreach (var item in persisted)
            {
                if (!chats.TryGetValue(item.ChatId, out var chat))
                    continue;
                if (chat.AuthorUserId == item.UserId)
                    continue; // không báo cho chính người đọc

                var isCustomer = item.UserRole == ActorRoleEnum.Customer;
                var account = isCustomer
                    ? customerById.GetValueOrDefault(item.UserId)
                    : staffById.GetValueOrDefault(item.UserId);

                if (!byTicketAndAuthor.TryGetValue(chat.TicketId, out var byAuthor))
                {
                    byAuthor = new Dictionary<Guid, List<ChatReaderDTO>>();
                    byTicketAndAuthor[chat.TicketId] = byAuthor;
                }

                if (!byAuthor.TryGetValue(chat.AuthorUserId, out var readers))
                {
                    readers = new List<ChatReaderDTO>();
                    byAuthor[chat.AuthorUserId] = readers;
                }

                readers.Add(new ChatReaderDTO
                {
                    ChatId = item.ChatId.ToString(),
                    UserId = item.UserId.ToString(),
                    DisplayName = account?.FullName ?? item.UserId.ToString(),
                    AvatarUrl = account?.AvatarUrl,
                    Role = item.UserRole,
                    ReadAt = item.ReadAt
                });
            }

            foreach (var (ticketId, byAuthor) in byTicketAndAuthor)
                await notifier.NotifyChatReadAsync(ticketId, byAuthor, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ChatReadReceiptBulkWriter failed to push ChatRead realtime for {Count} receipts.", persisted.Count);
        }
    }
}
