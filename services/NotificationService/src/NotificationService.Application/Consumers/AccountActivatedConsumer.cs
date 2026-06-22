using System.Text.Json;
using MassTransit;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — Account được kích hoạt → welcome notification cho chính account đó (AccountId là recipient).
/// Channel InApp (email welcome do EmailService lo riêng).
/// </summary>
public class AccountActivatedConsumer : IConsumer<AccountActivatedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public AccountActivatedConsumer(INotificationUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Consume(ConsumeContext<AccountActivatedEvent> context)
    {
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
