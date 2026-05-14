using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace BatteryService.Infrastructure.Consumers;

public class AccountActivatedConsumer : IConsumer<AccountActivatedEvent>
{
    private const string CustomerRole = "Customer";

    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IInboxStore _inboxStore;

    public AccountActivatedConsumer(IBatteryUnitOfWork unitOfWork, IInboxStore inboxStore)
    {
        _unitOfWork = unitOfWork;
        _inboxStore = inboxStore;
    }

    public async Task Consume(ConsumeContext<AccountActivatedEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(AccountActivatedConsumer), async () =>
        {
            var evt = context.Message;
            if (!evt.Roles.Any(role => string.Equals(role, CustomerRole, StringComparison.OrdinalIgnoreCase)))
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
                        RolesCsv = BuildRolesCsv(evt.Roles),
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
                    account.RolesCsv = BuildRolesCsv(evt.Roles);
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

    private static string BuildRolesCsv(IReadOnlyList<string> roles)
    {
        return string.Join(
            ',',
            roles
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Select(role => role.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
