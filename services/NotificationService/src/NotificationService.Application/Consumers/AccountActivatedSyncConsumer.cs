using MassTransit;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Entities;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-604 — Sync read-model account khi account active (upsert). Sync MỌI role
/// (khác BatteryService chỉ lọc Customer) vì NotificationService cần broadcast Manager/Admin.
/// Tách khỏi <see cref="NotificationAccountActivatedConsumer"/> (welcome notification) để 1 việc/1 consumer.
/// Lỗi DB → throw → MassTransit auto-retry. Upsert tự idempotent nên không cần InboxStore.
/// </summary>
public class AccountActivatedSyncConsumer : IConsumer<AccountActivatedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public AccountActivatedSyncConsumer(INotificationUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Consume(ConsumeContext<AccountActivatedEvent> context)
    {
        var evt = context.Message;

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            var account = await _unitOfWork.Accounts.GetAllAsync()
                .FirstOrDefaultAsync(x => x.Id == evt.AccountId, context.CancellationToken);

            if (account is null)
            {
                await _unitOfWork.Accounts.AddAsync(new AccountReadModel
                {
                    Id = evt.AccountId,
                    Email = evt.Email.Trim().ToLowerInvariant(),
                    FullName = evt.FullName.Trim(),
                    PhoneNumber = string.IsNullOrWhiteSpace(evt.PhoneNumber) ? null : evt.PhoneNumber.Trim(),
                    Role = evt.Role.Trim(),
                    IsActive = true,
                    IsDeleted = false,
                    DeletedAt = null,
                    LastSyncedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                account.Email = evt.Email.Trim().ToLowerInvariant();
                account.FullName = evt.FullName.Trim();
                account.PhoneNumber = string.IsNullOrWhiteSpace(evt.PhoneNumber) ? null : evt.PhoneNumber.Trim();
                account.Role = evt.Role.Trim();
                account.IsActive = true;
                account.IsDeleted = false;
                account.DeletedAt = null;
                account.LastSyncedAtUtc = DateTime.UtcNow;
                _unitOfWork.Accounts.UpdateAsync(account);
            }

            await _unitOfWork.CommitTransactionAsync();
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }
}
