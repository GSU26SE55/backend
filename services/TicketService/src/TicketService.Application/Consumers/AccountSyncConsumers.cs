using System.Linq;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.Consumers;

public class AccountActivatedConsumer : IConsumer<AccountActivatedEvent>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IInboxStore _inbox;

    public AccountActivatedConsumer(ITicketUnitOfWork uow, IInboxStore inbox)
    {
        _uow = uow;
        _inbox = inbox;
    }

    public async Task Consume(ConsumeContext<AccountActivatedEvent> context)
    {
        var @event = context.Message;
        if (!await _inbox.TryMarkProcessedAsync(context.MessageId.GetValueOrDefault(), nameof(AccountActivatedConsumer), context.CancellationToken))
            return;

        var isStaff = @event.Role.Equals("Staff", StringComparison.OrdinalIgnoreCase) ||
                      @event.Role.Equals("Manager", StringComparison.OrdinalIgnoreCase) ||
                      @event.Role.Equals("Technician", StringComparison.OrdinalIgnoreCase) ||
                      @event.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase);

        if (isStaff)
        {
            var staff = await _uow.StaffAccounts.GetAllAsync()
                .FirstOrDefaultAsync(s => s.AccountId == @event.AccountId, context.CancellationToken);

            if (staff == null)
            {
                staff = new StaffAccount
                {
                    Id = Guid.NewGuid(),
                    AccountId = @event.AccountId,
                    Email = @event.Email,
                    FullName = @event.FullName,
                    Status = AccountStatusEnum.Active,
                    LastSyncedAt = DateTime.UtcNow
                };
                await _uow.StaffAccounts.AddAsync(staff);
            }
            else
            {
                staff.Email = @event.Email;
                staff.FullName = @event.FullName;
                staff.Status = AccountStatusEnum.Active;
                staff.LastSyncedAt = DateTime.UtcNow;
                _uow.StaffAccounts.UpdateAsync(staff);
            }
        }
        else // Customer
        {
            var customer = await _uow.CustomerAccounts.GetAllAsync()
                .FirstOrDefaultAsync(c => c.AccountId == @event.AccountId, context.CancellationToken);

            if (customer == null)
            {
                customer = new CustomerAccount
                {
                    Id = Guid.NewGuid(),
                    AccountId = @event.AccountId,
                    Email = @event.Email,
                    FullName = @event.FullName,
                    PhoneNumber = @event.PhoneNumber,
                    Status = AccountStatusEnum.Active,
                    LastSyncedAt = DateTime.UtcNow
                };
                await _uow.CustomerAccounts.AddAsync(customer);
            }
            else
            {
                customer.Email = @event.Email;
                customer.FullName = @event.FullName;
                customer.PhoneNumber = @event.PhoneNumber;
                customer.Status = AccountStatusEnum.Active;
                customer.LastSyncedAt = DateTime.UtcNow;
                _uow.CustomerAccounts.UpdateAsync(customer);
            }
        }

        await _uow.SaveChangesAsync(context.CancellationToken);
    }
}

public class AccountStatusChangedConsumer : IConsumer<AccountStatusChangedEvent>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IInboxStore _inbox;

    public AccountStatusChangedConsumer(ITicketUnitOfWork uow, IInboxStore inbox)
    {
        _uow = uow;
        _inbox = inbox;
    }

    public async Task Consume(ConsumeContext<AccountStatusChangedEvent> context)
    {
        var @event = context.Message;
        if (!await _inbox.TryMarkProcessedAsync(context.MessageId.GetValueOrDefault(), nameof(AccountStatusChangedConsumer), context.CancellationToken))
            return;

        var staff = await _uow.StaffAccounts.GetAllAsync()
            .FirstOrDefaultAsync(s => s.AccountId == @event.AccountId, context.CancellationToken);
        if (staff != null)
        {
            staff.Status = (AccountStatusEnum)@event.NewStatus;
            staff.LastSyncedAt = DateTime.UtcNow;
            _uow.StaffAccounts.UpdateAsync(staff);
        }

        var customer = await _uow.CustomerAccounts.GetAllAsync()
            .FirstOrDefaultAsync(c => c.AccountId == @event.AccountId, context.CancellationToken);
        if (customer != null)
        {
            customer.Status = (AccountStatusEnum)@event.NewStatus;
            customer.LastSyncedAt = DateTime.UtcNow;
            _uow.CustomerAccounts.UpdateAsync(customer);
        }

        await _uow.SaveChangesAsync(context.CancellationToken);
    }
}

public class AccountProfileUpdatedConsumer : IConsumer<AccountProfileUpdatedEvent>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IInboxStore _inbox;

    public AccountProfileUpdatedConsumer(ITicketUnitOfWork uow, IInboxStore inbox)
    {
        _uow = uow;
        _inbox = inbox;
    }

    public async Task Consume(ConsumeContext<AccountProfileUpdatedEvent> context)
    {
        var @event = context.Message;
        if (!await _inbox.TryMarkProcessedAsync(context.MessageId.GetValueOrDefault(), nameof(AccountProfileUpdatedConsumer), context.CancellationToken))
            return;

        var staff = await _uow.StaffAccounts.GetAllAsync()
            .FirstOrDefaultAsync(s => s.AccountId == @event.AccountId, context.CancellationToken);
        if (staff != null)
        {
            staff.FullName = @event.FullName;
            staff.LastSyncedAt = DateTime.UtcNow;
            _uow.StaffAccounts.UpdateAsync(staff);
        }

        var customer = await _uow.CustomerAccounts.GetAllAsync()
            .FirstOrDefaultAsync(c => c.AccountId == @event.AccountId, context.CancellationToken);
        if (customer != null)
        {
            customer.FullName = @event.FullName;
            customer.PhoneNumber = @event.PhoneNumber;
            customer.LastSyncedAt = DateTime.UtcNow;
            _uow.CustomerAccounts.UpdateAsync(customer);
        }

        await _uow.SaveChangesAsync(context.CancellationToken);
    }
}

public class StaffProfileUpdatedConsumer : IConsumer<StaffProfileUpdatedEvent>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IInboxStore _inbox;

    public StaffProfileUpdatedConsumer(ITicketUnitOfWork uow, IInboxStore inbox)
    {
        _uow = uow;
        _inbox = inbox;
    }

    public async Task Consume(ConsumeContext<StaffProfileUpdatedEvent> context)
    {
        var @event = context.Message;
        if (!await _inbox.TryMarkProcessedAsync(context.MessageId.GetValueOrDefault(), nameof(StaffProfileUpdatedConsumer), context.CancellationToken))
            return;

        var staff = await _uow.StaffAccounts.GetAllAsync()
            .FirstOrDefaultAsync(s => s.AccountId == @event.AccountId, context.CancellationToken);
        if (staff != null)
        {
            staff.EmployeeCode = @event.EmployeeCode;
            staff.MaxConcurrentTickets = @event.MaxConcurrentTickets;
            staff.IsAvailable = @event.IsAvailable;
            staff.SkillTier = (StaffSkillTierEnum)@event.SkillTier;
            staff.LastSyncedAt = DateTime.UtcNow;
            _uow.StaffAccounts.UpdateAsync(staff);
            await _uow.SaveChangesAsync(context.CancellationToken);
        }
    }
}

public class StaffSkillsUpdatedConsumer : IConsumer<StaffSkillsUpdatedEvent>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IInboxStore _inbox;

    public StaffSkillsUpdatedConsumer(ITicketUnitOfWork uow, IInboxStore inbox)
    {
        _uow = uow;
        _inbox = inbox;
    }

    public async Task Consume(ConsumeContext<StaffSkillsUpdatedEvent> context)
    {
        var @event = context.Message;
        if (!await _inbox.TryMarkProcessedAsync(context.MessageId.GetValueOrDefault(), nameof(StaffSkillsUpdatedConsumer), context.CancellationToken))
            return;

        var staff = await _uow.StaffAccounts.GetAllAsync()
            .FirstOrDefaultAsync(s => s.AccountId == @event.AccountId, context.CancellationToken);
        if (staff != null)
        {
            staff.SkillCodes = @event.SkillCodes;
            staff.LastSyncedAt = DateTime.UtcNow;
            _uow.StaffAccounts.UpdateAsync(staff);
            await _uow.SaveChangesAsync(context.CancellationToken);
        }
    }
}
