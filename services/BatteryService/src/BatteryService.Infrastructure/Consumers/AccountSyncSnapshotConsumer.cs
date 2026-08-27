using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace BatteryService.Infrastructure.Consumers;

/// <summary>
/// Rebuilds BatteryService's customer read model from AuthService snapshots.
/// This is the recovery path for accounts created while BatteryService was unavailable
/// or for a database that was reset independently from AuthService.
/// </summary>
public class AccountSyncSnapshotConsumer : IConsumer<AccountSyncSnapshotEvent>
{
    private const string CustomerRole = "Customer";

    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IInboxStore _inboxStore;
    private readonly ILogger<AccountSyncSnapshotConsumer> _logger;

    public AccountSyncSnapshotConsumer(
        IBatteryUnitOfWork unitOfWork,
        IInboxStore inboxStore,
        ILogger<AccountSyncSnapshotConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _inboxStore = inboxStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AccountSyncSnapshotEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(AccountSyncSnapshotConsumer), async () =>
        {
            var evt = context.Message;
            var isCustomer = string.Equals(evt.Role, CustomerRole, StringComparison.OrdinalIgnoreCase);
            var account = await _unitOfWork.CustomerAccounts
                .GetAllAsync()
                .FirstOrDefaultAsync(item => item.Id == evt.AccountId, context.CancellationToken);

            // BatteryService only mirrors customers. A deleted/non-customer account that has never
            // owned battery data does not need a row here.
            if (account is null && (!isCustomer || evt.IsDeleted))
                return;

            // So sánh bằng mốc PHÁT SINH ở AuthService, không dùng thời điểm consumer chạy. Nhờ đó
            // snapshot định kỳ vẫn sửa được dữ liệu bị chỉnh tay, nhưng snapshot cũ không thể ghi
            // đè một lifecycle event mới hơn.
            if (account?.LastSourceEventAtUtc is { } applied && applied >= evt.SnapshotAtUtc)
                return;

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                if (account is null)
                {
                    account = new CustomerAccount
                    {
                        Id = evt.AccountId,
                        Email = evt.Email.Trim().ToLowerInvariant(),
                        FullName = evt.FullName.Trim(),
                        PhoneNumber = NormalizePhone(evt.PhoneNumber),
                        Role = evt.Role.Trim(),
                        IsActive = evt.AccountStatus == 1,
                        IsDeleted = false,
                        DeletedAt = null,
                        LastSyncedAtUtc = DateTime.UtcNow,
                        LastSourceEventAtUtc = evt.SnapshotAtUtc
                    };
                    await _unitOfWork.CustomerAccounts.AddAsync(account);
                }
                else
                {
                    account.Email = evt.Email.Trim().ToLowerInvariant();
                    account.FullName = evt.FullName.Trim();
                    account.PhoneNumber = NormalizePhone(evt.PhoneNumber);
                    account.Role = evt.Role.Trim();
                    account.IsActive = isCustomer && evt.AccountStatus == 1 && !evt.IsDeleted;
                    account.LastSyncedAtUtc = DateTime.UtcNow;
                    account.LastSourceEventAtUtc = evt.SnapshotAtUtc;

                    if (evt.IsDeleted)
                    {
                        _unitOfWork.CustomerAccounts.DeleteAsync(account);
                    }
                    else if (isCustomer)
                    {
                        // Reactivation: chỉ projection của role Customer hiện tại được hồi sinh.
                        account.IsDeleted = false;
                        account.DeletedAt = null;
                        _unitOfWork.CustomerAccounts.UpdateAsync(account);
                    }
                    else
                    {
                        // Giữ row role cũ (và trạng thái soft-delete nếu có) để asset lịch sử còn
                        // truy ngược được chủ cũ, nhưng tuyệt đối không coi nó là Customer active.
                        _unitOfWork.CustomerAccounts.UpdateAsync(account);
                    }
                }

                await _unitOfWork.CommitTransactionAsync();
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }

            _logger.LogInformation(
                "Synced Battery customer mirror account={AccountId} role={Role} active={IsActive} deleted={IsDeleted}.",
                evt.AccountId, evt.Role, evt.IsActive, evt.IsDeleted);
        });
    }

    private static string? NormalizePhone(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
