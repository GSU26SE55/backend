using MassTransit;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Consumers;

/// <summary>
/// Soft-delete mọi projection của account đã bị xoá ở AuthService. Row vẫn còn để các ticket lịch
/// sử giữ nguyên FK, nhưng không còn được dùng cho phân công hay thao tác mới.
/// </summary>
public sealed class TicketAccountDeletedConsumer : IConsumer<AccountDeletedEvent>
{
    private readonly ITicketUnitOfWork _unitOfWork;
    private readonly IInboxStore _inboxStore;

    public TicketAccountDeletedConsumer(ITicketUnitOfWork unitOfWork, IInboxStore inboxStore)
    {
        _unitOfWork = unitOfWork;
        _inboxStore = inboxStore;
    }

    public async Task Consume(ConsumeContext<AccountDeletedEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(TicketAccountDeletedConsumer), async () =>
        {
            var evt = context.Message;
            var now = DateTime.UtcNow;
            var changed = false;

            var staff = await _unitOfWork.StaffAccounts.GetAllAsync()
                .FirstOrDefaultAsync(item => item.AccountId == evt.AccountId, context.CancellationToken);
            if (staff is not null
                && (staff.LastSourceEventAtUtc is null || staff.LastSourceEventAtUtc < evt.OccurredAt))
            {
                staff.Status = AccountStatusEnum.Inactive;
                staff.LastSyncedAt = now;
                staff.LastSourceEventAtUtc = evt.OccurredAt;
                _unitOfWork.StaffAccounts.DeleteAsync(staff);
                changed = true;
            }

            var customer = await _unitOfWork.CustomerAccounts.GetAllAsync()
                .FirstOrDefaultAsync(item => item.AccountId == evt.AccountId, context.CancellationToken);
            if (customer is not null
                && (customer.LastSourceEventAtUtc is null || customer.LastSourceEventAtUtc < evt.OccurredAt))
            {
                customer.Status = AccountStatusEnum.Inactive;
                customer.LastSyncedAt = now;
                customer.LastSourceEventAtUtc = evt.OccurredAt;
                _unitOfWork.CustomerAccounts.DeleteAsync(customer);
                changed = true;
            }

            if (changed)
                await _unitOfWork.SaveChangesAsync(context.CancellationToken);
        });
    }
}
