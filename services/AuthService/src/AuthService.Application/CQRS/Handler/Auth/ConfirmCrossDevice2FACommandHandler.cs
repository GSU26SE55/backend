using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Auth;

/// <summary>
/// #AUTH-51: Device B (phone) confirm 2FA setup bằng token + TOTP code.
/// Verify TOTP với secret từ Redis → enable 2FA cho account → remove token.
/// </summary>
public class ConfirmCrossDevice2FACommandHandler : IRequestHandler<ConfirmCrossDevice2FACommand, CommonResponse<string>>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly ITotpService _totp;
    private readonly ITwoFactorSecretProtector _protector;
    private readonly ITwoFactorCrossDeviceConfirmStore _store;
    private readonly IPublisher _publisher;

    public ConfirmCrossDevice2FACommandHandler(
        IAuthUnitOfWork unitOfWork,
        ITotpService totp,
        ITwoFactorSecretProtector protector,
        ITwoFactorCrossDeviceConfirmStore store,
        IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _totp = totp;
        _protector = protector;
        _store = store;
        _publisher = publisher;
    }

    public async Task<CommonResponse<string>> Handle(ConfirmCrossDevice2FACommand request, CancellationToken cancellationToken)
    {
        var data = await _store.GetAsync(request.ConfirmToken, cancellationToken);
        if (data == null)
        {
            await _publisher.Publish(new AuditTrailNotification(
                AuditActionEnum.TwoFactorSetupCrossDeviceExpired, request.AccountId, IsSuccess: false,
                Reason: "Token expired or invalid"), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Fail(404, "The confirmation link has expired or is invalid. Please request a new one from the original device.");
        }

        // Security: token chỉ confirm được bởi cùng account đã request (chống stolen link từ email).
        // Device B PHẢI đang login bằng cùng account (chứ không phải copy link đưa người khác).
        if (data.AccountId != request.AccountId)
        {
            await _publisher.Publish(new AuditTrailNotification(
                AuditActionEnum.TwoFactorSetupCrossDeviceExpired, request.AccountId, IsSuccess: false,
                Reason: "AccountId mismatch — token was issued for a different account",
                Metadata: new Dictionary<string, object?>
                {
                    ["expectedAccountId"] = data.AccountId,
                    ["actualAccountId"] = request.AccountId
                }), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Fail(403, "This confirmation link does not belong to your account.");
        }

        var account = await _unitOfWork.Accounts.GetAllAsync()
            .Where(a => !a.IsDeleted)
            .FirstOrDefaultAsync(a => a.Id == request.AccountId, cancellationToken);
        if (account == null)
            return Fail(404, "Account not found.");

        if (account.TwoFactorEnabled && !string.IsNullOrEmpty(account.TwoFactorSecret))
        {
            // Race: user đã enable 2FA bằng flow khác → token vô nghĩa, remove luôn.
            await _store.RemoveAsync(request.ConfirmToken, cancellationToken);
            return Fail(409, "2FA was already enabled before this confirmation. The token has been removed.");
        }

        // Verify TOTP với secret từ Redis (plaintext, chưa protect).
        if (!_totp.VerifyCode(data.Secret, request.TotpCode))
        {
            // KHÔNG remove token — cho phép user retry với code mới (TOTP rotate mỗi 30s).
            await _publisher.Publish(new AuditTrailNotification(
                AuditActionEnum.OtpVerifyFailed, account.Id, IsSuccess: false,
                Reason: "Wrong TOTP in cross-device confirm"), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Fail(422, "Invalid TOTP code. Please try again.");
        }

        // Enable 2FA: protect secret + set EncryptedAt.
        account.TwoFactorSecret = _protector.Protect(data.Secret);
        account.TwoFactorSecretEncryptedAt = DateTime.UtcNow;
        account.TwoFactorEnabled = true;
        _unitOfWork.Accounts.UpdateAsync(account);

        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.TwoFactorSetupCrossDeviceConfirmed, account.Id, IsSuccess: true,
            TargetEmail: account.Email,
            ActorAccountIdOverride: account.Id,
            Metadata: new Dictionary<string, object?>
            {
                ["requestingSessionId"] = data.RequestingSessionId
            }), cancellationToken);
        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.TwoFactorEnabled, account.Id, IsSuccess: true,
            TargetEmail: account.Email,
            ActorAccountIdOverride: account.Id,
            Metadata: new Dictionary<string, object?> { ["method"] = "cross-device" }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Remove token (one-shot) — sau khi save thành công.
        await _store.RemoveAsync(request.ConfirmToken, cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "2FA enabled successfully. The original device will refresh its status automatically.",
            Data = account.Id.ToString()
        };
    }

    private static CommonResponse<string> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
