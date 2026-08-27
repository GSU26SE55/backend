using BatteryService.Application.Interfaces;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace BatteryService.Infrastructure.Consumers;

public class AccountStatusChangedConsumer : IConsumer<AccountStatusChangedEvent>
{
    private const int ActiveStatus = 1;

    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IInboxStore _inboxStore;

    public AccountStatusChangedConsumer(IBatteryUnitOfWork unitOfWork, IInboxStore inboxStore)
    {
        _unitOfWork = unitOfWork;
        _inboxStore = inboxStore;
    }

    public async Task Consume(ConsumeContext<AccountStatusChangedEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(AccountStatusChangedConsumer), async () =>
        {
            var evt = context.Message;

            var account = await _unitOfWork.CustomerAccounts
                .GetAllAsync()
                .FirstOrDefaultAsync(item => item.Id == evt.AccountId, context.CancellationToken);

            if (account is null)
                return;

            if (account.LastSourceEventAtUtc is { } applied && applied >= evt.OccurredAt)
                return;

            await _unitOfWork.BeginTransactionAsync();

            try
            {
                account.Email = evt.Email.Trim().ToLowerInvariant();
                var currentRole = string.IsNullOrWhiteSpace(evt.Role) ? account.Role : evt.Role;
                account.Role = currentRole.Trim();
                account.IsActive = currentRole.Equals("Customer", StringComparison.OrdinalIgnoreCase)
                    && evt.NewStatus == ActiveStatus;
                account.LastSyncedAtUtc = DateTime.UtcNow;
                account.LastSourceEventAtUtc = evt.OccurredAt;
                _unitOfWork.CustomerAccounts.UpdateAsync(account);

                await _unitOfWork.CommitTransactionAsync();
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        });
    }
}
