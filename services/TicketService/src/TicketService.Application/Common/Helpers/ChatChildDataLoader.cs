using Microsoft.EntityFrameworkCore;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.Common.Helpers;

/// <summary>
/// Batch-load mention/reaction theo danh sách <c>chatId</c> để populate <c>TicketChatDTO.Mentions</c>/<c>Reactions</c>
/// — tránh N+1 khi map nhiều chat trong 1 page (#536/#539).
/// </summary>
public static class ChatChildDataLoader
{
    public static async Task<Dictionary<Guid, List<TicketChatMentionDTO>>> LoadMentionsAsync(
        ITicketUnitOfWork uow, IReadOnlyCollection<Guid> chatIds, CancellationToken ct)
    {
        if (chatIds.Count == 0)
            return new();

        var mentions = await uow.TicketChatMentions.GetAllAsync()
            .AsNoTracking()
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
                IsAcknowledged = m.IsAcknowledged,
                AcknowledgedAt = m.AcknowledgedAt,
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
}
