using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events.Chats;
using SharedInfrastructure.Idempotency;
using StackExchange.Redis;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Consumers;

public class ChatLanguageDetectConsumer : IConsumer<ChatCreatedEvent>
{
    private readonly ILanguageDetectionService _langDetector;
    private readonly ITicketUnitOfWork _uow;
    private readonly IInboxStore _inbox;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ChatLanguageDetectConsumer> _logger;

    public ChatLanguageDetectConsumer(
        ILanguageDetectionService langDetector,
        ITicketUnitOfWork uow,
        IInboxStore inbox,
        IConnectionMultiplexer redis,
        ILogger<ChatLanguageDetectConsumer> logger)
    {
        _langDetector = langDetector;
        _uow = uow;
        _inbox = inbox;
        _redis = redis;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ChatCreatedEvent> context)
    {
        var @event = context.Message;
        var messageId = context.MessageId.GetValueOrDefault();

        if (!await _inbox.TryMarkProcessedAsync(messageId, nameof(ChatLanguageDetectConsumer), context.CancellationToken))
            return;

        var detected = _langDetector.Detect(@event.Body);
        if (detected == "und")
            return;

        var chat = await _uow.TicketChats
            .GetAllAsync()
            .Where(c => !c.IsDeleted && c.Id == @event.ChatId)
            .FirstOrDefaultAsync(context.CancellationToken);

        if (chat == null)
        {
            _logger.LogWarning("[ChatLanguageDetect] Chat {ChatId} not found, skipping.", @event.ChatId);
            return;
        }

        if (chat.OriginalLanguage != null)
            return;

        await _uow.BeginTransactionAsync();
        try
        {
            chat.OriginalLanguage = detected;
            _uow.TicketChats.UpdateAsync(chat);
            await _uow.CommitTransactionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatLanguageDetect] Failed to update OriginalLanguage for chat {ChatId}.", @event.ChatId);
            await _uow.RollbackTransactionAsync();

            // Xóa inbox key để MassTransit retry được xử lý lại sau khi sập
            try
            {
                var inboxKey = $"inbox:{nameof(ChatLanguageDetectConsumer)}:{messageId:N}";
                await _redis.GetDatabase().KeyDeleteAsync(inboxKey);
            }
            catch (RedisException redisEx)
            {
                _logger.LogWarning(redisEx, "[ChatLanguageDetect] Failed to delete inbox key for message {MessageId}.", messageId);
            }

            throw;
        }
    }
}
