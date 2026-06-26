using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — Account được kích hoạt → welcome notification cho chính account đó (AccountId là recipient).
/// Channel InApp (email welcome do EmailService lo riêng).
/// </summary>
public class AccountActivatedConsumer : IConsumer<AccountActivatedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly ILogger<AccountActivatedConsumer> _logger;

    public AccountActivatedConsumer(
        INotificationUnitOfWork unitOfWork,
        ICacheService cache,
        ILogger<AccountActivatedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AccountActivatedEvent> context)
    {
        var messageId = context.MessageId ?? Guid.Empty;
        if (messageId != Guid.Empty && !await NotificationDebounce.TryBeginByMessageAsync(_cache, messageId, context.CancellationToken))
        {
            _logger.LogInformation("Debounce: skip duplicate AccountActivated message={MessageId}", messageId);
            return;
        }

        var evt = context.Message;

        var recipientIds = new[] { evt.AccountId };

        var title = "Chào mừng bạn đến với hệ thống";
        var body = $"Tài khoản của {evt.FullName} đã được kích hoạt thành công.";
        var payload = JsonSerializer.Serialize(new
        {
            accountId = evt.AccountId,
            role = evt.Role,
            creationSource = evt.CreationSource,
            screen = "Home"
        });

        await NotificationWriter.WriteAsync(
            _unitOfWork, recipientIds, NotificationTypeEnum.AccountActivated, NotificationWriter.InAppOnly,
            title, body, payload, "Account", evt.AccountId, context.CancellationToken);
    }
}
