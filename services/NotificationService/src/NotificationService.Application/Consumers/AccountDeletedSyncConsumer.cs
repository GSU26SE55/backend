using MassTransit;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.Interfaces.Repositories;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-604 — Account bị soft-delete → đánh dấu read-model IsActive=false + soft delete.
/// Sau đó resolver sẽ loại account này khỏi recipient. Bỏ qua nếu record chưa tồn tại.
/// Lỗi DB → throw → MassTransit auto-retry.
/// </summary>
public class AccountDeletedSyncConsumer : IConsumer<AccountDeletedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public AccountDeletedSyncConsumer(INotificationUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Consume(ConsumeContext<AccountDeletedEvent> context)
    {
        var evt = context.Message;

        var account = await _unitOfWork.Accounts.GetAllAsync()
            .FirstOrDefaultAsync(x => x.Id == evt.AccountId, context.CancellationToken);

        if (account is null)
            return;

        if (account.LastSnapshotAtUtc is { } applied && applied >= evt.OccurredAt)
            return;

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            account.IsActive = false;
            account.LastSyncedAtUtc = DateTime.UtcNow;
            account.LastSnapshotAtUtc = evt.OccurredAt;
            _unitOfWork.Accounts.DeleteAsync(account);

            await _unitOfWork.CommitTransactionAsync();
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }
}
