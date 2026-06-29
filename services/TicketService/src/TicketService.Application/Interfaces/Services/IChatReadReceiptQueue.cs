using System.Threading.Channels;
using TicketService.Domain.Enums;

namespace TicketService.Application.Interfaces.Services;

public record ChatReadReceiptItem(Guid ChatId, Guid UserId, ActorRoleEnum UserRole, DateTime ReadAt);

/// <summary>
/// Hàng đợi in-memory cho read receipt — <c>ChatMarkAsReadCommandHandler</c> enqueue,
/// <c>ChatReadReceiptBulkWriter</c> (BackgroundService) drain theo batch để giảm DB pressure (#542).
/// </summary>
public interface IChatReadReceiptQueue
{
    bool TryEnqueue(ChatReadReceiptItem item);
    ChannelReader<ChatReadReceiptItem> Reader { get; }
}
