using MassTransit;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Entities;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// Applies status changes that do not necessarily pass through the snapshot publisher (for
/// example automatic lockout/unlock during login). This keeps role-based notification recipient
/// resolution aligned with AuthService between periodic reconciliation ticks.
/// </summary>
public sealed class AccountStatusChangedSyncConsumer : IConsumer<AccountStatusChangedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public AccountStatusChangedSyncConsumer(INotificationUnitOfWork unitOfWork)
        => _unitOfWork = unitOfWork;

    public async Task Consume(ConsumeContext<AccountStatusChangedEvent> context)
    {
        var evt = context.Message;
        var account = await _unitOfWork.Accounts.GetAllAsync()
            .FirstOrDefaultAsync(item => item.Id == evt.AccountId, context.CancellationToken);

        if (account?.LastSnapshotAtUtc is { } applied && applied >= evt.OccurredAt)
            return;

        // Older contract versions did not carry a complete account snapshot. Do not create an
        // unusable role-less row from such a message; the periodic full snapshot will repair it.
        if (account is null && string.IsNullOrWhiteSpace(evt.Role))
            return;

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            if (account is null)
            {
                account = new AccountReadModel
                {
                    Id = evt.AccountId,
                    Email = evt.Email.Trim().ToLowerInvariant(),
                    FullName = evt.FullName.Trim(),
                    PhoneNumber = Normalize(evt.PhoneNumber),
                    Role = evt.Role.Trim(),
                    IsActive = evt.IsActive,
                    IsDeleted = false,
                    DeletedAt = null,
                    LastSyncedAtUtc = DateTime.UtcNow,
                    LastSnapshotAtUtc = evt.OccurredAt
                };
                await _unitOfWork.Accounts.AddAsync(account);
            }
            else
            {
                account.Email = evt.Email.Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(evt.FullName))
                    account.FullName = evt.FullName.Trim();
                if (!string.IsNullOrWhiteSpace(evt.Role))
                {
                    account.Role = evt.Role.Trim();
                    account.PhoneNumber = Normalize(evt.PhoneNumber);
                    account.IsActive = evt.IsActive;
                }
                else
                {
                    // Compatibility with messages published before IsActive was added.
                    account.IsActive = evt.NewStatus is 1 or 2;
                }

                account.LastSyncedAtUtc = DateTime.UtcNow;
                account.LastSnapshotAtUtc = evt.OccurredAt;
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

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Applies role changes immediately; periodic snapshots remain the convergence safety net.
/// </summary>
public sealed class AccountRoleChangedSyncConsumer : IConsumer<AccountRoleChangedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public AccountRoleChangedSyncConsumer(INotificationUnitOfWork unitOfWork)
        => _unitOfWork = unitOfWork;

    public async Task Consume(ConsumeContext<AccountRoleChangedEvent> context)
    {
        var evt = context.Message;
        var account = await _unitOfWork.Accounts.GetAllAsync()
            .FirstOrDefaultAsync(item => item.Id == evt.AccountId, context.CancellationToken);

        if (account?.LastSnapshotAtUtc is { } applied && applied >= evt.ChangedAtUtc)
            return;

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            if (account is null)
            {
                account = new AccountReadModel
                {
                    Id = evt.AccountId,
                    Email = evt.Email.Trim().ToLowerInvariant(),
                    FullName = evt.FullName.Trim(),
                    PhoneNumber = Normalize(evt.PhoneNumber),
                    Role = evt.NewRole.Trim(),
                    IsActive = evt.AccountStatus is 1 or 2,
                    IsDeleted = false,
                    DeletedAt = null,
                    LastSyncedAtUtc = DateTime.UtcNow,
                    LastSnapshotAtUtc = evt.ChangedAtUtc
                };
                await _unitOfWork.Accounts.AddAsync(account);
            }
            else
            {
                account.Email = evt.Email.Trim().ToLowerInvariant();
                account.FullName = evt.FullName.Trim();
                account.PhoneNumber = Normalize(evt.PhoneNumber);
                account.Role = evt.NewRole.Trim();
                account.IsActive = evt.AccountStatus is 1 or 2;
                account.IsDeleted = false;
                account.DeletedAt = null;
                account.LastSyncedAtUtc = DateTime.UtcNow;
                account.LastSnapshotAtUtc = evt.ChangedAtUtc;
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

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
