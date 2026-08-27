using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Auth;

/// <summary>
/// #AUTH-50: verify OTP rồi restore account (clear soft-delete flags + set Status=Active).
///
/// 02/08/2026 — phát thêm <see cref="AccountSyncSnapshotEvent"/>. Đây là đường DUY NHẤT hồi sinh
/// một account đã xoá mềm. Trước bản vá này nó không phát event nào, nên read-model bên
/// NotificationService vẫn giữ dòng đã xoá mềm + <c>IsActive = false</c>: tài khoản khôi phục xong
/// nhưng vĩnh viễn không nhận được thông báo nào nữa.
/// </summary>
public class ReactivateVerifyCommandHandler : IRequestHandler<ReactivateVerifyCommand, CommonResponse<string>>
{
    private static readonly TimeSpan ReactivationWindow = TimeSpan.FromDays(90);

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-11
    private readonly IMessageProducerService _messageProducer;

    public ReactivateVerifyCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPublisher publisher,
        IMessageProducerService messageProducer)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
        _messageProducer = messageProducer;
    }

    public async Task<CommonResponse<string>> Handle(ReactivateVerifyCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = EmailNormalizer.Normalize(request.Email);
        var windowCutoff = DateTime.UtcNow - ReactivationWindow;

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .IgnoreQueryFilters()
            .Include(a => a.Role)
            .FirstOrDefaultAsync(a =>
                a.Email.ToLower() == normalizedEmail
                && a.IsDeleted
                && a.DeletedAt.HasValue
                && a.DeletedAt.Value >= windowCutoff,
                cancellationToken);

        if (account == null)
            return Fail(404, "Account not found within the restore window, or the OTP is invalid.");

        if (!account.OtpExpiredAt.HasValue || account.OtpExpiredAt.Value <= DateTime.UtcNow
            || string.IsNullOrEmpty(account.OtpCode))
            return Fail(401, "OTP has expired. Please request a new OTP.");

        if (!SecureCompareHelper.FixedTimeEquals(account.OtpCode, request.Otp.Trim()))
            return Fail(401, "Incorrect OTP.");

        // Restore: clear soft-delete + reset auth state.
        var oldStatus = account.Status;   // GH-766 — bắt trước khi ghi đè.
        account.IsDeleted = false;
        account.DeletedAt = null;
        account.Status = AccountStatusEnum.Active;
        account.OtpCode = null;
        account.OtpExpiredAt = null;
        account.OtpPurpose = null;
        account.FailedLoginAttempts = 0;
        account.LockoutEndAt = null;
        _unitOfWork.Accounts.UpdateAsync(account);

        // #AUDIT-11
        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.AccountStatusChanged, account.Id, true, TargetEmail: account.Email,
            Metadata: new Dictionary<string, object?> { ["newStatus"] = "Active", ["reason"] = "Reactivated" }), cancellationToken);

        // Outbox: atomic với SaveChangesAsync bên dưới. IsDeleted=false để read-model bỏ soft-delete
        // đã đánh dấu lúc account bị xoá.
        await _messageProducer.PublishAsync(new AccountSyncSnapshotEvent(
            account.Id,
            account.Email,
            account.FullName,
            account.PhoneNumber,
            account.Role?.Name ?? string.Empty,
            IsActive: AccountStatusEnum.Active.IsNotifiable(),
            IsDeleted: false,
            SnapshotAtUtc: DateTime.UtcNow,
            Reason: "Reactivated",
            AccountStatus: (int)AccountStatusEnum.Active), cancellationToken);

        // GH-766 — Battery/Ticket đồng bộ trạng thái account qua event này; trước đây không nơi nào
        // publish nó nên hai read-model kia vẫn thấy account Active sau khi bị khoá/vô hiệu hoá.
        await _messageProducer.PublishAsync(new AccountStatusChangedEvent(
            account.Id,
            account.Email,
            (int)oldStatus,
            (int)AccountStatusEnum.Active,
            "Reactivated",
            Role: account.Role?.Name ?? string.Empty,
            FullName: account.FullName,
            PhoneNumber: account.PhoneNumber,
            IsActive: true), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Account restored successfully. Please log in again.",
            Data = account.Id.ToString()
        };
    }

    private static CommonResponse<string> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };
}
