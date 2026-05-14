using BatteryService.Application.Interfaces;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace BatteryService.Infrastructure.Consumers;

public class AccountDeletedConsumer : IConsumer<AccountDeletedEvent>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IInboxStore _inboxStore;

    public AccountDeletedConsumer(IBatteryUnitOfWork unitOfWork, IInboxStore inboxStore)
    {
        _unitOfWork = unitOfWork;
        _inboxStore = inboxStore;
    }

    public async Task Consume(ConsumeContext<AccountDeletedEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(AccountDeletedConsumer), async () =>
        {
            var evt = context.Message;

            var account = await _unitOfWork.CustomerAccounts
                .GetAllAsync()
                .FirstOrDefaultAsync(item => item.Id == evt.AccountId, context.CancellationToken);

            if (account is null)
                return;

            await _unitOfWork.BeginTransactionAsync();

            try
            {
                account.IsActive = false;
                account.LastSyncedAtUtc = DateTime.UtcNow;
                _unitOfWork.CustomerAccounts.DeleteAsync(account);

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
