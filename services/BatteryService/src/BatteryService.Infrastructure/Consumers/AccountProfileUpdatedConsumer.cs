using BatteryService.Application.Interfaces;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace BatteryService.Infrastructure.Consumers;

/// <summary>
/// GH-773 — đồng bộ hồ sơ khách hàng vào bản sao của BatteryService.
/// </summary>
/// <remarks>
/// <para>
/// Chính chú thích của hợp đồng <see cref="AccountProfileUpdatedEvent"/> ghi BatteryService là một
/// subscriber, Ticket và Notification đều đã có consumer — riêng Battery thì không. Bản sao khách
/// hàng vì thế đứng yên ở giá trị chụp lúc kích hoạt: danh sách site và danh sách pin hiển thị tên
/// và số điện thoại cũ vĩnh viễn, kể cả sau khi khách tự sửa hồ sơ.
/// </para>
/// <para>
/// Chỉ chạm các trường HỒ SƠ. Trạng thái và role có event riêng
/// (<see cref="AccountStatusChangedEvent"/>, <see cref="AccountRoleChangedEvent"/>) — chép thêm ở
/// đây sẽ tạo hai đường ghi cùng một ô, và event nào tới sau sẽ thắng bất kể cái nào mới hơn.
/// </para>
/// </remarks>
public class AccountProfileUpdatedConsumer : IConsumer<AccountProfileUpdatedEvent>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IInboxStore _inboxStore;

    public AccountProfileUpdatedConsumer(IBatteryUnitOfWork unitOfWork, IInboxStore inboxStore)
    {
        _unitOfWork = unitOfWork;
        _inboxStore = inboxStore;
    }

    public async Task Consume(ConsumeContext<AccountProfileUpdatedEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(AccountProfileUpdatedConsumer), async () =>
        {
            var evt = context.Message;

            // Bản sao dùng chính AccountId làm khoá chính (xem BatteryAccountActivatedConsumer).
            var account = await _unitOfWork.CustomerAccounts
                .GetAllAsync()
                .FirstOrDefaultAsync(item => item.Id == evt.AccountId, context.CancellationToken);

            // Không có bản sao ⇒ account này chưa từng là khách hàng của BatteryService. Tạo mới ở
            // đây sẽ dựng ra khách hàng không có tài sản nào — việc gán tài sản là của luồng khác.
            if (account is null)
                return;

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                // Chuẩn hoá chữ thường khớp AccountStatusChangedConsumer — lệch nhau thì cùng một
                // account có hai dạng email tuỳ event nào tới sau, và tra cứu theo email sẽ hụt.
                account.Email = evt.Email.Trim().ToLowerInvariant();
                account.FullName = evt.FullName;
                account.PhoneNumber = evt.PhoneNumber;
                account.LastSyncedAtUtc = DateTime.UtcNow;

                _unitOfWork.CustomerAccounts.UpdateAsync(account);
                await _unitOfWork.CommitTransactionAsync();
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;   // ProcessOnceAsync nhả chỗ giữ ⇒ MassTransit thử lại thật (GH-764).
            }
        });
    }
}
