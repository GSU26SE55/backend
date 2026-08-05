using BatteryService.Application.Interfaces;
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
/// Không tạo mới bản sao khi role chuyển THÀNH Customer mà trước đó chưa có: pin được gán cho
/// customer nào là việc của luồng gán tài sản, không phải của một sự kiện đổi role. Ở đây chỉ
/// cập nhật cái đã tồn tại — làm hơn thế là dựng ra khách hàng không có tài sản nào.
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

            if (account is null)
                return;

            var isCustomer = evt.NewRole.Equals(CustomerRole, StringComparison.OrdinalIgnoreCase);

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                account.Email = evt.Email.Trim().ToLowerInvariant();
                account.FullName = evt.FullName;
                account.PhoneNumber = evt.PhoneNumber;
                account.Role = evt.NewRole;
                // Rời khỏi Customer ⇒ ngừng coi là khách hàng đang hoạt động. Giữ lại bản ghi để
                // pin đã gán vẫn truy ngược được chủ cũ, thay vì xoá và làm mồ côi dữ liệu.
                account.IsActive = isCustomer;
                account.LastSyncedAtUtc = DateTime.UtcNow;

                _unitOfWork.CustomerAccounts.UpdateAsync(account);
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
