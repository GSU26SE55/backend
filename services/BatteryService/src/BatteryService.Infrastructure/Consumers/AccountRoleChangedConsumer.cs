using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace BatteryService.Infrastructure.Consumers;

/// <summary>
/// GH-769 — đồng bộ bản sao khách hàng khi role đổi.
/// </summary>
/// <remarks>
/// <para>
/// BatteryService chỉ mirror KHÁCH HÀNG (pin được gán cho customer). Bản sao được dựng đúng một
/// lần lúc account kích hoạt, nên sau khi đổi role nó giữ nguyên giá trị cũ: một người đã chuyển
/// sang Staff vẫn nằm đó dưới dạng khách hàng đang hoạt động.
/// </para>
/// <para>
/// Khi role chuyển THÀNH Customer phải tạo projection ngay cả khi chưa có pin. Bảng này là bản sao
/// account Customer, không phải bảng ownership của pin; bỏ qua ở đây làm count lệch vĩnh viễn nếu
/// event Activated ban đầu xảy ra lúc account còn là Staff/Manager/Admin.
/// </para>
/// </remarks>
public class AccountRoleChangedConsumer : IConsumer<AccountRoleChangedEvent>
{
    private const string CustomerRole = "Customer";

    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IInboxStore _inboxStore;
    private readonly ILogger<AccountRoleChangedConsumer> _logger;

    public AccountRoleChangedConsumer(
        IBatteryUnitOfWork unitOfWork,
        IInboxStore inboxStore,
        ILogger<AccountRoleChangedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _inboxStore = inboxStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AccountRoleChangedEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(AccountRoleChangedConsumer), async () =>
        {
            var evt = context.Message;

            var account = await _unitOfWork.CustomerAccounts
                .GetAllAsync()
                .FirstOrDefaultAsync(item => item.Id == evt.AccountId, context.CancellationToken);

            var isCustomer = evt.NewRole.Equals(CustomerRole, StringComparison.OrdinalIgnoreCase);

            if (account is null && !isCustomer)
                return;

            if (account?.LastSourceEventAtUtc is { } applied && applied >= evt.ChangedAtUtc)
                return;

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                if (account is null)
                {
                    await _unitOfWork.CustomerAccounts.AddAsync(new CustomerAccount
                    {
                        Id = evt.AccountId,
                        Email = evt.Email.Trim().ToLowerInvariant(),
                        FullName = evt.FullName.Trim(),
                        PhoneNumber = string.IsNullOrWhiteSpace(evt.PhoneNumber) ? null : evt.PhoneNumber.Trim(),
                        Role = evt.NewRole.Trim(),
                        IsActive = evt.AccountStatus == 1,
                        IsDeleted = false,
                        DeletedAt = null,
                        LastSyncedAtUtc = DateTime.UtcNow,
                        LastSourceEventAtUtc = evt.ChangedAtUtc,
                    });
                }
                else
                {
                    account.Email = evt.Email.Trim().ToLowerInvariant();
                    account.FullName = evt.FullName.Trim();
                    account.PhoneNumber = string.IsNullOrWhiteSpace(evt.PhoneNumber) ? null : evt.PhoneNumber.Trim();
                    account.Role = evt.NewRole.Trim();
                    // Rời khỏi Customer ⇒ ngừng coi là khách hàng đang hoạt động. Giữ lại bản ghi
                    // để pin lịch sử vẫn truy ngược được chủ cũ.
                    account.IsActive = isCustomer && evt.AccountStatus == 1;
                    account.LastSyncedAtUtc = DateTime.UtcNow;
                    account.LastSourceEventAtUtc = evt.ChangedAtUtc;
                    if (isCustomer)
                    {
                        account.IsDeleted = false;
                        account.DeletedAt = null;
                    }

                    _unitOfWork.CustomerAccounts.UpdateAsync(account);
                }
                await _unitOfWork.CommitTransactionAsync();
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;   // ProcessOnceAsync nhả chỗ giữ ⇒ MassTransit thử lại thật (GH-764).
            }

            _logger.LogInformation(
                "Account {AccountId} đổi role {OldRole} → {NewRole}; bản sao khách hàng isActive={IsActive}.",
                evt.AccountId, evt.OldRole, evt.NewRole, isCustomer);
        });
    }
}
