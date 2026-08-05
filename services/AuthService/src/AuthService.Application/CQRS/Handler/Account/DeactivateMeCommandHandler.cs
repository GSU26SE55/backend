using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Account;

/// <summary>
/// Người dùng tự vô hiệu hoá tài khoản của mình (Status → Inactive) và thu hồi toàn bộ refresh token.
///
/// 02/08/2026 — phát thêm <see cref="AccountSyncSnapshotEvent"/>. Đây là một trong hai đường tự
/// phục vụ làm tài khoản ngừng hoạt động mà trước đó không phát event tích hợp nào, nên người đã
/// tự vô hiệu hoá vẫn nằm nguyên trong danh sách nhận thông báo của NotificationService.
/// </summary>
public class DeactivateMeCommandHandler : IRequestHandler<DeactivateMeCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-11
    private readonly IMessageProducerService _messageProducer;

    public DeactivateMeCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPublisher publisher,
        IMessageProducerService messageProducer)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
        _messageProducer = messageProducer;
    }

    public async Task<AccountActionResponse> Handle(DeactivateMeCommand request, CancellationToken cancellationToken)
    {
        // Include(Role) để dựng snapshot — RoleId nullable nên Role có thể null.
        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .Include(a => a.Role)
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);
        if (account == null)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy tài khoản."
            };
        }

        var oldStatus = account.Status;   // GH-766 — bắt trước khi ghi đè, để event nói đúng chuyển đổi.
        account.Status = AccountStatusEnum.Inactive;
        _unitOfWork.Accounts.UpdateAsync(account);

        var activeTokens = await _unitOfWork.RefreshTokens
            .GetAllAsync()
            .Where(rt => rt.AccountId == account.Id && rt.Status == RefreshTokenStatus.Active)
            .ToListAsync(cancellationToken);

        foreach (var rt in activeTokens)
        {
            rt.Status = RefreshTokenStatus.Revoked;
            rt.RevokedAt = DateTime.UtcNow;
            rt.RevokedReason = "Self deactivated";
            _unitOfWork.RefreshTokens.UpdateAsync(rt);
        }

        // #AUDIT-11
        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.AccountDeactivated, account.Id, true, TargetEmail: account.Email), cancellationToken);

        // Outbox: atomic với SaveChangesAsync bên dưới.
        await _messageProducer.PublishAsync(new AccountSyncSnapshotEvent(
            account.Id,
            account.Email,
            account.FullName,
            account.PhoneNumber,
            account.Role?.Name ?? string.Empty,
            IsActive: AccountStatusEnum.Inactive.IsNotifiable(),
            IsDeleted: false,
            SnapshotAtUtc: DateTime.UtcNow,
            Reason: "StatusChanged"), cancellationToken);

        // GH-766 — Battery/Ticket đồng bộ trạng thái account qua event này; trước đây không nơi nào
        // publish nó nên hai read-model kia vẫn thấy account Active sau khi bị khoá/vô hiệu hoá.
        await _messageProducer.PublishAsync(new AccountStatusChangedEvent(
            account.Id,
            account.Email,
            (int)oldStatus,
            (int)AccountStatusEnum.Inactive,
            "Self deactivated"), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đã vô hiệu hóa tài khoản. Liên hệ admin để khôi phục.",
            Data = account.Id
        };
    }
}
