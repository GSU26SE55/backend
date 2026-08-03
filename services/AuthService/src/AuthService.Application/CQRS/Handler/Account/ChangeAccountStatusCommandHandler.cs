using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Account;

/// <summary>
/// Đổi trạng thái account (khoá / đình chỉ / cấm / mở lại) và thu hồi refresh token nếu cần.
///
/// 02/08/2026 — phát thêm <see cref="AccountSyncSnapshotEvent"/>. Trước đó handler này không phát
/// event tích hợp nào: <c>AccountStatusChangedEvent</c> có sẵn trong SharedContracts và cả
/// TicketService lẫn BatteryService đều đã viết consumer cho nó, nhưng KHÔNG NƠI NÀO trong code
/// chạy thật publish event đó — nên khoá tài khoản xong, read-model bên NotificationService vẫn
/// <c>IsActive = true</c> và người bị khoá vẫn tiếp tục được đưa vào danh sách nhận thông báo.
/// </summary>
public class ChangeAccountStatusCommandHandler : IRequestHandler<ChangeAccountStatusCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;
    private readonly IMessageProducerService _messageProducer;

    public ChangeAccountStatusCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPublisher publisher,
        IMessageProducerService messageProducer)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
        _messageProducer = messageProducer;
    }

    public async Task<AccountActionResponse> Handle(ChangeAccountStatusCommand request, CancellationToken cancellationToken)
    {
        // Include(Role) để dựng snapshot — RoleId nullable (account Google OAuth chưa onboard),
        // khi đó Role = chuỗi rỗng và không nhóm nào resolve trúng account này.
        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .Include(a => a.Role)
            .FirstOrDefaultAsync(a => a.Id == request.Id && !a.IsDeleted, cancellationToken);
        if (account == null)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy tài khoản."
            };
        }

        if (account.Status == request.Status)
        {
            return new AccountActionResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Trạng thái không thay đổi.",
                Data = account.Id
            };
        }

        var oldStatus = account.Status;
        account.Status = request.Status;

        if (request.Status != AccountStatusEnum.Locked)
            account.LockoutEndAt = null;

        if (request.Status == AccountStatusEnum.Active)
            account.FailedLoginAttempts = 0;

        _unitOfWork.Accounts.UpdateAsync(account);

        var revokeStatuses = new[]
        {
            AccountStatusEnum.Inactive,
            AccountStatusEnum.Suspended,
            AccountStatusEnum.Banned,
            AccountStatusEnum.Locked
        };

        var revokedCount = 0;
        if (revokeStatuses.Contains(request.Status))
        {
            var activeTokens = await _unitOfWork.RefreshTokens
                .GetAllAsync()
                .Where(rt => rt.AccountId == account.Id && rt.Status == RefreshTokenStatus.Active)
                .ToListAsync(cancellationToken);

            foreach (var rt in activeTokens)
            {
                rt.Status = RefreshTokenStatus.Revoked;
                rt.RevokedAt = DateTime.UtcNow;
                rt.RevokedReason = $"Account status changed to {request.Status}. {request.Reason ?? string.Empty}".Trim();
                _unitOfWork.RefreshTokens.UpdateAsync(rt);
            }
            revokedCount = activeTokens.Count;
        }

        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.AccountStatusChanged, account.Id, IsSuccess: true,
            TargetEmail: account.Email,
            Reason: request.Reason,
            Metadata: new Dictionary<string, object?>
            {
                ["oldStatus"] = (int)oldStatus,
                ["oldStatusName"] = oldStatus.ToString(),
                ["newStatus"] = (int)request.Status,
                ["newStatusName"] = request.Status.ToString(),
                ["revokedSessions"] = revokedCount
            }), cancellationToken);

        // Outbox: INSERT vào OutboxMessages của cùng DbContext → atomic với SaveChangesAsync bên dưới.
        await _messageProducer.PublishAsync(new AccountSyncSnapshotEvent(
            account.Id,
            account.Email,
            account.FullName,
            account.PhoneNumber,
            account.Role?.Name ?? string.Empty,
            IsActive: request.Status.IsNotifiable(),
            IsDeleted: false,
            SnapshotAtUtc: DateTime.UtcNow,
            Reason: "StatusChanged"), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cập nhật trạng thái tài khoản thành công.",
            Data = account.Id
        };
    }
}
