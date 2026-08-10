using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events.Chats;
using SharedInfrastructure.Idempotency;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Consumers;

public class ChatLanguageDetectConsumer : IConsumer<ChatCreatedEvent>
{
    private readonly ILanguageDetectionService _langDetector;
    private readonly ITicketUnitOfWork _uow;
    private readonly IInboxStore _inbox;
    private readonly ILogger<ChatLanguageDetectConsumer> _logger;

    public ChatLanguageDetectConsumer(
        ILanguageDetectionService langDetector,
        ITicketUnitOfWork uow,
        IInboxStore inbox,
        ILogger<ChatLanguageDetectConsumer> logger)
    {
        _langDetector = langDetector;
        _uow = uow;
        _inbox = inbox;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ChatCreatedEvent> context)
    {
        var @event = context.Message;

        // GH-764 — dùng chung cơ chế Inbox (giữ chỗ → chạy → chốt/nhả) thay cho việc tự xoá khoá
        // Redis bằng tay. Bản cũ đánh dấu đã-xử-lý TRƯỚC side effect rồi tự gỡ trong `catch`, với
        // định dạng khoá viết cứng ngay tại đây — nghĩa là mỗi consumer phải tự nhớ làm việc đó,
        // và chỉ gỡ được đúng nhánh lỗi mà người viết nghĩ ra. Nay việc nhả nằm ở một chỗ duy nhất.
        await context.ProcessOnceAsync(_inbox, nameof(ChatLanguageDetectConsumer), async () =>
        {
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
                throw;   // ProcessOnceAsync nhả chỗ giữ, MassTransit thử lại.
            }
        });
    }
}
