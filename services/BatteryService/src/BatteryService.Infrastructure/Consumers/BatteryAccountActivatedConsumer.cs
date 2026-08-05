using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace BatteryService.Infrastructure.Consumers;

public class BatteryAccountActivatedConsumer : IConsumer<AccountActivatedEvent>
{
    private const string CustomerRole = "Customer";

    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IInboxStore _inboxStore;

    public BatteryAccountActivatedConsumer(IBatteryUnitOfWork unitOfWork, IInboxStore inboxStore)
    {
        _unitOfWork = unitOfWork;
        _inboxStore = inboxStore;
    }

    public async Task Consume(ConsumeContext<AccountActivatedEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(BatteryAccountActivatedConsumer), async () =>
        {
            var evt = context.Message;

            // Quan hệ 1-N: account chỉ có 1 role. Chỉ sync account có role Customer vào BatteryService.
            if (!string.Equals(evt.Role, CustomerRole, StringComparison.OrdinalIgnoreCase))
                return;

            await _unitOfWork.BeginTransactionAsync();

            try
            {
                var account = await _unitOfWork.CustomerAccounts
                    .GetAllAsync()
                    .FirstOrDefaultAsync(item => item.Id == evt.AccountId, context.CancellationToken);

                if (account is null)
                {
                    await _unitOfWork.CustomerAccounts.AddAsync(new CustomerAccount
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
                    _unitOfWork.CustomerAccounts.UpdateAsync(account);
                }

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
