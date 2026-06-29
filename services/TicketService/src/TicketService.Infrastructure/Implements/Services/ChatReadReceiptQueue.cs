using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Singleton — wrap 1 <see cref="Channel{T}"/> bounded dùng chung giữa producer (command handler)
/// và consumer (<c>ChatReadReceiptBulkWriter</c>). Bounded + DropWrite vì mark-read là best-effort,
/// không được phép block hoặc làm fail request chính (#542).
/// </summary>
public class ChatReadReceiptQueue : IChatReadReceiptQueue
{
    private const int Capacity = 10_000;
    private readonly Channel<ChatReadReceiptItem> _channel;
    private readonly ILogger<ChatReadReceiptQueue> _logger;

    public ChatReadReceiptQueue(ILogger<ChatReadReceiptQueue> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<ChatReadReceiptItem>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelReader<ChatReadReceiptItem> Reader => _channel.Reader;

    public bool TryEnqueue(ChatReadReceiptItem item)
    {
        var written = _channel.Writer.TryWrite(item);
        if (!written)
            _logger.LogWarning("ChatReadReceiptQueue full — dropped read receipt for ChatId={ChatId}, UserId={UserId}", item.ChatId, item.UserId);

        return written;
    }
}
