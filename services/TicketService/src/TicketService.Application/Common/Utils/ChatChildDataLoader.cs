using Microsoft.EntityFrameworkCore;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.Common.Utils;

/// <summary>
/// Batch-load mention/reaction theo danh sách <c>chatId</c> để populate <c>TicketChatDTO.Mentions</c>/<c>Reactions</c>
/// — tránh N+1 khi map nhiều chat trong 1 page (#536/#539).
/// </summary>
public static class ChatChildDataLoader
{
    /// <summary>
    /// Tập chatId mà <paramref name="actorUserId"/> đã đọc (bảng TicketChatRead) — dùng
    /// để set <c>TicketChatDTO.IsRead</c>, từ đó client vẽ mốc "Tin nhắn chưa đọc".
    /// KHÔNG được nhét kết quả này vào cache trang chat: cache dùng chung giữa các user.
    /// </summary>
    public static async Task<HashSet<Guid>> LoadReadChatIdsForUserAsync(
        ITicketUnitOfWork uow,
        IReadOnlyCollection<Guid> chatIds,
        Guid actorUserId,
        CancellationToken ct)
    {
        if (chatIds.Count == 0)
            return new();

        // IsRead thuần hiển thị (vẽ mốc "chưa đọc"), KHÔNG phải authz. Repo chưa được cấu
        // hình (một số unit test chỉ mock repo mà handler thực sự dùng) thì coi như chưa có
        // read-receipt nào thay vì ném NullReference làm hỏng cả luồng đọc chat.
        var source = uow.TicketChatReads?.GetAllAsync();
        if (source is null)
            return new();

        var readIds = await source
            .AsNoTracking()
            .Where(r => chatIds.Contains(r.ChatId) && r.UserId == actorUserId && !r.IsDeleted)
            .Select(r => r.ChatId)
            .ToListAsync(ct);

        return readIds.ToHashSet();
    }

    /// <summary>
    /// Batch-load "ai đã đọc" cho danh sách chat — nguồn cho <c>TicketChatDTO.ReadReceipts</c>
    /// (tick "đã xem" kiểu Messenger). Gom tên + avatar 1 lần cho cả trang, không N+1.
    ///
    /// Chỉ nên truyền vào chatId của những tin do CHÍNH actor gửi: người gửi mới cần biết ai đã xem,
    /// nạp cho cả trang chỉ làm phình payload.
    /// </summary>
    public static async Task<Dictionary<Guid, List<ChatReaderDTO>>> LoadReadReceiptsAsync(
        ITicketUnitOfWork uow,
        IReadOnlyCollection<Guid> chatIds,
        CancellationToken ct)
    {
        if (chatIds.Count == 0)
            return new();

        // Repo có thể chưa được mock trong unit test cũ — receipt thuần hiển thị, thiếu thì coi
        // như chưa ai đọc chứ không ném NullReference làm hỏng cả luồng đọc chat.
        var source = uow.TicketChatReads?.GetAllAsync();
        if (source is null)
            return new();

        var reads = await source
            .AsNoTracking()
            .Where(r => chatIds.Contains(r.ChatId) && !r.IsDeleted)
            .OrderBy(r => r.ReadAt)
            .Select(r => new { r.ChatId, r.UserId, r.UserRole, r.ReadAt })
            .ToListAsync(ct);

        if (reads.Count == 0)
            return new();

        var readerIds = reads.Select(r => r.UserId).Distinct().ToList();

        var customers = await uow.CustomerAccounts.GetAllAsync()
            .AsNoTracking()
            .Where(a => readerIds.Contains(a.AccountId) && !a.IsDeleted)
            .Select(a => new { a.AccountId, a.FullName, a.AvatarUrl })
            .ToListAsync(ct);
        var staff = await uow.StaffAccounts.GetAllAsync()
            .AsNoTracking()
            .Where(a => readerIds.Contains(a.AccountId) && !a.IsDeleted)
            .Select(a => new { a.AccountId, a.FullName, a.AvatarUrl })
            .ToListAsync(ct);

        var customerById = customers.ToDictionary(a => a.AccountId);
        var staffById = staff.ToDictionary(a => a.AccountId);

        return reads
            .GroupBy(r => r.ChatId)
            .ToDictionary(g => g.Key, g => g.Select(r =>
            {
                var isCustomer = r.UserRole == ActorRoleEnum.Customer;
                var account = isCustomer
                    ? customerById.GetValueOrDefault(r.UserId)
                    : staffById.GetValueOrDefault(r.UserId);

                return new ChatReaderDTO
                {
                    ChatId = r.ChatId.ToString(),
                    UserId = r.UserId.ToString(),
                    DisplayName = account?.FullName ?? r.UserId.ToString(),
                    AvatarUrl = account?.AvatarUrl,
                    Role = r.UserRole,
                    ReadAt = r.ReadAt
                };
            }).ToList());
    }

    public static async Task<Dictionary<Guid, List<TicketChatMentionDTO>>> LoadMentionsAsync(
        ITicketUnitOfWork uow, IReadOnlyCollection<Guid> chatIds, CancellationToken ct)
    {
        if (chatIds.Count == 0)
            return new();

        var mentions = await uow.TicketChatMentions.GetAllAsync()
            .AsNoTracking()
            .Include(m => m.Chat)
            .Where(m => chatIds.Contains(m.ChatId) && !m.IsDeleted)
            .ToListAsync(ct);

        return mentions
            .GroupBy(m => m.ChatId)
            .ToDictionary(g => g.Key, g => g.Select(m => new TicketChatMentionDTO
            {
                Id = m.Id.ToString(),
                ChatId = m.ChatId.ToString(),
                MentionedUserId = m.MentionedUserId.ToString(),
                MentionedUserRole = m.MentionedUserRole,
                MentionedDisplayName = m.MentionedDisplayName,
                IsInternal = m.Chat.IsInternal,
                CreatedAt = m.CreatedAt
            }).ToList());
    }

    public static async Task<Dictionary<Guid, TicketChatReactionsAggregateDTO>> LoadReactionsAsync(
        ITicketUnitOfWork uow, IReadOnlyCollection<Guid> chatIds, CancellationToken ct)
    {
        if (chatIds.Count == 0)
            return new();

        var reactions = await uow.TicketChatReactions.GetAllAsync()
            .AsNoTracking()
            .Where(r => chatIds.Contains(r.ChatId) && !r.IsDeleted)
            .ToListAsync(ct);

        return reactions
            .GroupBy(r => r.ChatId)
            .ToDictionary(g => g.Key, g => ChatReactionAggregateHelper.Build(g));
    }

    /// <summary>
    /// Batch-load bản dịch của <paramref name="actorUserId"/> cho danh sách chat — tránh N+1.
    /// Trả về bản dịch mới nhất (theo CreatedAt) mỗi chat nếu user đã từng dịch.
    /// </summary>
    public static async Task<Dictionary<Guid, ChatTranslateDTO>> LoadTranslationsForUserAsync(
        ITicketUnitOfWork uow,
        IReadOnlyCollection<Guid> chatIds,
        Guid actorUserId,
        CancellationToken ct)
    {
        if (chatIds.Count == 0)
            return new();

        var userLinks = await uow.ChatTranslationUsers.GetAllAsync()
            .AsNoTracking()
            .Include(tu => tu.Translation)
            .Where(tu => !tu.IsDeleted
                      && tu.UserId == actorUserId
                      && !tu.Translation.IsDeleted
                      && chatIds.Contains(tu.Translation.ChatId))
            .OrderByDescending(tu => tu.CreatedAt)
            .ToListAsync(ct);

        return userLinks
            .GroupBy(tu => tu.Translation.ChatId)
            .ToDictionary(g => g.Key, g =>
            {
                var t = g.First().Translation;
                return new ChatTranslateDTO
                {
                    TranslatedBody = t.TranslatedBody,
                    TargetLanguage = t.TargetLanguage,
                    Provider = t.Provider.ToString(),
                    FromCache = false,
                };
            });
    }
}
