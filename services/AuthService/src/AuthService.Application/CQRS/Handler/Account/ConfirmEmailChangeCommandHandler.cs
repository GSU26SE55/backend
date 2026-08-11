using System.Security.Cryptography;
using System.Text;
using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using StackExchange.Redis;

namespace AuthService.Application.CQRS.Handler.Account;

public class ConfirmEmailChangeCommandHandler : IRequestHandler<ConfirmEmailChangeCommand, AccountActionResponse>
{
    private const int MaxFailedAttempts = 5;
    private const int LockoutDurationMinutes = 15;
    private const string EmailReserveKeyPrefix = "email_reserve:";

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IConnectionMultiplexer _redis;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-11

    public ConfirmEmailChangeCommandHandler(IAuthUnitOfWork unitOfWork, IConnectionMultiplexer redis, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _redis = redis;
        _publisher = publisher;
    }

    public async Task<AccountActionResponse> Handle(ConfirmEmailChangeCommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);
        if (account == null)
            return Fail(404, "Account not found.");

        if (string.IsNullOrEmpty(account.PendingEmail) || account.OtpPurpose != OtpPurposeEnum.EmailChange)
            return Fail(409, "There is no pending email change request to verify.");

        if (account.LockoutEndAt.HasValue && account.LockoutEndAt.Value > DateTime.UtcNow)
            return Fail(423, "Account is locked. Please try again later.");

        if (!account.OtpExpiredAt.HasValue || account.OtpExpiredAt.Value <= DateTime.UtcNow)
            return Fail(401, "OTP has expired. Please request the email change again.");

        if (!SecureCompareHelper.FixedTimeEquals(account.OtpCode, request.Otp.Trim()))
        {
            account.FailedLoginAttempts += 1;
            if (account.FailedLoginAttempts >= MaxFailedAttempts)
                account.LockoutEndAt = DateTime.UtcNow.AddMinutes(LockoutDurationMinutes);
            _unitOfWork.Accounts.UpdateAsync(account);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Fail(401, "Invalid OTP.");
        }

        // Double-check email vẫn chưa bị account khác chiếm trong khoảng chờ verify.
        var pending = account.PendingEmail!;
        var taken = await _unitOfWork.Accounts
            .GetAllAsync()
            .AnyAsync(a => a.Id != account.Id && a.Email.ToLower() == pending.ToLower() && !a.IsDeleted, cancellationToken);
        if (taken)
        {
            account.PendingEmail = null;
            account.OtpCode = null;
            account.OtpPurpose = null;
            account.OtpExpiredAt = null;
            _unitOfWork.Accounts.UpdateAsync(account);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Fail(409, "New email was registered by another account while verification was pending.");
        }

        account.Email = pending;
        account.EmailConfirmed = true;
        account.PendingEmail = null;
        account.OtpCode = null;
        account.OtpPurpose = null;
        account.OtpExpiredAt = null;
        account.FailedLoginAttempts = 0;
        account.LockoutEndAt = null;
        _unitOfWork.Accounts.UpdateAsync(account);

        // Force re-login mọi thiết bị (vì email = identity).
        var activeTokens = await _unitOfWork.RefreshTokens
            .GetAllAsync()
            .Where(rt => rt.AccountId == account.Id && rt.Status == RefreshTokenStatus.Active)
            .ToListAsync(cancellationToken);
        foreach (var rt in activeTokens)
        {
            rt.Status = RefreshTokenStatus.Revoked;
            rt.RevokedAt = DateTime.UtcNow;
            rt.RevokedReason = "Email changed";
            _unitOfWork.RefreshTokens.UpdateAsync(rt);
        }

        // #AUDIT-11
        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.EmailChangeConfirmed, account.Id, true, TargetEmail: account.Email), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // #AUTH-24: release Redis reservation cho email mới (best-effort, key tự expire 5p nếu fail — #AUTH-14).
        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(BuildReserveKey(pending));
        }
        catch { /* swallow — reservation key tự expire, không phải critical path. */ }

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Email changed successfully. Please log in again with the new email.",
            Data = account.Id
        };
    }

    private static AccountActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };

    private static string BuildReserveKey(string normalizedEmail)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail));
        return EmailReserveKeyPrefix + Convert.ToHexString(bytes)[..16];
    }
}
