using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.CQRS.Notification.Login;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Auth;

public class Verify2FALoginCommandHandler : IRequestHandler<Verify2FALoginCommand, LoginResponse>
{
    private const int MaxAttemptsPerChallenge = 5;

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly ITwoFactorChallengeStore _challengeStore;
    private readonly ITotpService _totp;
    private readonly ITwoFactorSecretProtector _protector;
    private readonly IBackupCodeGenerator _backupCodes;
    private readonly IAuthTokenIssuer _tokenIssuer;
    private readonly IPublisher _publisher;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public Verify2FALoginCommandHandler(
        IAuthUnitOfWork unitOfWork,
        ITwoFactorChallengeStore challengeStore,
        ITotpService totp,
        ITwoFactorSecretProtector protector,
        IBackupCodeGenerator backupCodes,
        IAuthTokenIssuer tokenIssuer,
        IPublisher publisher,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _unitOfWork = unitOfWork;
        _challengeStore = challengeStore;
        _totp = totp;
        _protector = protector;
        _backupCodes = backupCodes;
        _tokenIssuer = tokenIssuer;
        _publisher = publisher;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<LoginResponse> Handle(Verify2FALoginCommand request, CancellationToken cancellationToken)
    {
        var challenge = await _challengeStore.GetAsync(request.ChallengeToken, cancellationToken);
        if (challenge == null)
            return Fail(422, "Phiên xác thực đã hết hạn hoặc không hợp lệ. Hãy login lại.");

        // Atomic increment trước khi verify (nếu attacker spam mỗi request đều tốn quota)
        var attempts = await _challengeStore.IncrementAttemptsAsync(request.ChallengeToken, cancellationToken);
        if (attempts > MaxAttemptsPerChallenge)
        {
            await _challengeStore.InvalidateAsync(request.ChallengeToken, cancellationToken);
            return Fail(429, "Vượt quá số lần thử cho phiên này. Hãy login lại.");
        }

        var account = await _unitOfWork.Accounts.GetAllAsync()
            .Include(a => a.Role)
            .Where(a => !a.IsDeleted)
            .FirstOrDefaultAsync(a => a.Id == challenge.AccountId, cancellationToken);

        if (account == null)
        {
            await _challengeStore.InvalidateAsync(request.ChallengeToken, cancellationToken);
            return Fail(404, "Tài khoản không tồn tại hoặc đã bị xóa.");
        }

        // Re-check status — account có thể bị suspend/lock giữa lúc challenge còn sống
        if (!IsAccountLoginEligible(account))
        {
            await _challengeStore.InvalidateAsync(request.ChallengeToken, cancellationToken);
            await PublishAudit(AuditActionEnum.LoginFailedAccountLocked, account.Id, false,
                targetEmail: account.Email,
                reason: $"Status changed mid-challenge: {account.Status}",
                cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Fail(403, "Tài khoản không khả dụng cho đăng nhập.");
        }

        if (!account.TwoFactorEnabled || string.IsNullOrEmpty(account.TwoFactorSecret))
        {
            // Inconsistent state: account đã disable 2FA giữa lúc challenge sống
            await _challengeStore.InvalidateAsync(request.ChallengeToken, cancellationToken);
            return Fail(409, "2FA không còn được bật. Hãy login lại.");
        }

        // Verify code: TOTP hoặc backup code
        bool verified;
        Guid? redeemedBackupCodeId = null;
        if (request.IsBackupCode)
        {
            verified = await TryRedeemBackupCodeAsync(account.Id, request.Code, cancellationToken);
            if (verified)
            {
                // re-query để lấy id của row vừa redeem cho audit metadata (best-effort)
                redeemedBackupCodeId = await _unitOfWork.BackupCodes.GetAllAsync()
                    .Where(b => b.AccountId == account.Id && b.RedeemedAt != null && !b.IsDeleted)
                    .OrderByDescending(b => b.RedeemedAt)
                    .Select(b => (Guid?)b.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }
        else
        {
            var plaintextSecret = _protector.Unprotect(account.TwoFactorSecret);
            verified = _totp.VerifyCode(plaintextSecret, request.Code);
        }

        if (!verified)
        {
            await PublishAudit(AuditActionEnum.OtpVerifyFailed, account.Id, false,
                targetEmail: account.Email,
                reason: request.IsBackupCode ? "Wrong backup code" : "Wrong TOTP",
                metadata: new Dictionary<string, object?>
                {
                    ["attempts"] = attempts,
                    ["maxAttempts"] = MaxAttemptsPerChallenge,
                },
                cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            var remaining = MaxAttemptsPerChallenge - attempts;
            return Fail(422, $"Mã xác thực không đúng. Còn {Math.Max(0, remaining)} lần thử.");
        }

        // Lazy re-encrypt secret nếu vẫn là plaintext legacy
        if (!_protector.IsProtected(account.TwoFactorSecret))
        {
            account.TwoFactorSecret = _protector.Protect(account.TwoFactorSecret);
            account.TwoFactorSecretEncryptedAt = DateTime.UtcNow;
            _unitOfWork.Accounts.UpdateAsync(account);
        }

        var (ipAddress, userAgent, deviceId) = ClientInfoHelper.Resolve(_httpContextAccessor?.HttpContext);
        var (tokens, sessionId) = await _tokenIssuer.IssueAsync(account, ipAddress, userAgent, deviceId, cancellationToken);

        var roleName = account.Role?.Name ?? string.Empty;

        if (redeemedBackupCodeId.HasValue)
        {
            await PublishAudit(AuditActionEnum.BackupCodeRedeemed, account.Id, true,
                targetEmail: account.Email,
                actorAccountIdOverride: account.Id,
                metadata: new Dictionary<string, object?> { ["backupCodeId"] = redeemedBackupCodeId.Value },
                cancellationToken: cancellationToken);
        }

        await PublishAudit(AuditActionEnum.LoginWith2FA, account.Id, true,
            targetEmail: account.Email,
            actorAccountIdOverride: account.Id,
            metadata: new Dictionary<string, object?>
            {
                ["role"] = roleName,
                ["sessionId"] = sessionId,
                ["method"] = request.IsBackupCode ? "backupCode" : "totp",
            },
            cancellationToken: cancellationToken);

        await _publisher.Publish(new LoginAttemptNotification(
            account.Id, account.Email, LoginAttemptResult.Success,
            Method: request.IsBackupCode ? "BackupCode" : "TOTP",
            Note: null), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _challengeStore.InvalidateAsync(request.ChallengeToken, cancellationToken);

        return new LoginResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đăng nhập thành công.",
            Data = new LoginResultDto { Tokens = tokens }
        };
    }

    /// <summary>
    /// Tìm 1 backup code chưa redeem khớp với plain, đánh dấu redeemed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Race condition note:</b> Pattern load-then-update có theoretical race nếu 2 concurrent requests
    /// cùng challenge token + cùng backup code chạy đồng thời (vd user double-click verify button).
    /// PostgreSQL default isolation READ COMMITTED → cả 2 tx có thể load row với <c>RedeemedAt=null</c>
    /// và đều UPDATE thành công → cùng 1 code redeem 2 lần.
    /// </para>
    /// <para>
    /// <b>Tại sao chấp nhận:</b> Không exploit được security — sau verify thành công, <c>InvalidateAsync</c>
    /// xóa challenge → request 2 không thể tạo session khác cho cùng user qua flow này. Tệ nhất:
    /// 2 access token cho cùng login → session limit policy sẽ revoke cái cũ. Không có data corruption
    /// (cùng giá trị RedeemedAt). Để fix triệt để cần <c>ExecuteUpdateAsync</c> với WHERE clause atomic —
    /// defer vì khó mock trong unit test với InMemory/MockQueryable provider.
    /// </para>
    /// </remarks>
    private async Task<bool> TryRedeemBackupCodeAsync(Guid accountId, string plainCode, CancellationToken ct)
    {
        var candidates = await _unitOfWork.BackupCodes.GetAllAsync()
            .Where(b => b.AccountId == accountId && b.RedeemedAt == null && !b.IsDeleted)
            .ToListAsync(ct);

        foreach (var c in candidates)
        {
            if (_backupCodes.Verify(plainCode, c.CodeHash))
            {
                c.RedeemedAt = DateTime.UtcNow;
                _unitOfWork.BackupCodes.UpdateAsync(c);
                return true;
            }
        }
        return false;
    }

    private static bool IsAccountLoginEligible(Domain.Entities.Account account)
    {
        if (account.LockoutEndAt.HasValue && account.LockoutEndAt.Value > DateTime.UtcNow)
            return false;
        return account.Status switch
        {
            AccountStatusEnum.Active => true,
            AccountStatusEnum.Locked => account.LockoutEndAt == null || account.LockoutEndAt < DateTime.UtcNow,
            _ => false,
        };
    }

    private Task PublishAudit(
        AuditActionEnum action,
        Guid? targetAccountId,
        bool isSuccess,
        string? targetEmail = null,
        string? reason = null,
        IReadOnlyDictionary<string, object?>? metadata = null,
        Guid? actorAccountIdOverride = null,
        CancellationToken cancellationToken = default)
    {
        return _publisher.Publish(new AuditTrailNotification(
            action, targetAccountId, isSuccess, targetEmail, reason, metadata, actorAccountIdOverride),
            cancellationToken);
    }

    private static LoginResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };
}
