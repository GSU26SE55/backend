using MassTransit;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.Interfaces.Repositories;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-604 — Cập nhật display info (Email/FullName/PhoneNumber) của read-model account khi profile đổi.
/// KHÔNG đổi Role (event không mang Role). Bỏ qua nếu record chưa tồn tại — Activated mới là nơi tạo
/// (event này không đủ thông tin role để tạo mới). Lỗi DB → throw → MassTransit auto-retry.
/// </summary>
public class AccountProfileUpdatedSyncConsumer : IConsumer<AccountProfileUpdatedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public AccountProfileUpdatedSyncConsumer(INotificationUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Consume(ConsumeContext<AccountProfileUpdatedEvent> context)
    {
        var evt = context.Message;

        var account = await _unitOfWork.Accounts.GetAllAsync()
            .FirstOrDefaultAsync(x => x.Id == evt.AccountId, context.CancellationToken);

        if (account is null)
            return;

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            account.Email = evt.Email.Trim().ToLowerInvariant();
            account.FullName = evt.FullName.Trim();
            account.PhoneNumber = string.IsNullOrWhiteSpace(evt.PhoneNumber) ? null : evt.PhoneNumber.Trim();
            account.LastSyncedAtUtc = DateTime.UtcNow;
            _unitOfWork.Accounts.UpdateAsync(account);

            await _unitOfWork.CommitTransactionAsync();
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }
}
