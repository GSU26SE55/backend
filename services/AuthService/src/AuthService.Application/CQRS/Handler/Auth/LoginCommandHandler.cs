using System.Security.Cryptography;
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
using SharedInfrastructure.Metrics;

namespace AuthService.Application.CQRS.Handler.Auth;

public class LoginCommandHandler : IRequestHandler<LoginCommand, LoginResponse>
{
    private const int MaxFailedAttempts = 5;
    private const int LockoutDurationMinutes = 15;
    private static readonly TimeSpan ChallengeTtl = TimeSpan.FromMinutes(5);

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuthTokenIssuer _tokenIssuer;
    private readonly ITwoFactorChallengeStore _challengeStore;
    private readonly IPublisher _publisher;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public LoginCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IAuthTokenIssuer tokenIssuer,
        ITwoFactorChallengeStore challengeStore,
        IPublisher publisher,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _tokenIssuer = tokenIssuer;
        _challengeStore = challengeStore;
        _publisher = publisher;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = EmailNormalizer.Normalize(request.Email);

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .Include(a => a.Role)
            .FirstOrDefaultAsync(a => a.Email.ToLower() == normalizedEmail && !a.IsDeleted, cancellationToken);

        if (account == null)
        {
            // #AUTH-17: dùng cùng audit action với "sai mật khẩu" + jitter delay để time đáp ứng
            // miss path ~= match BCrypt verify (~100-200ms) → khó phân biệt qua side-channel.
            await ApplyEnumerationDelay(cancellationToken);

            await PublishAudit(AuditActionEnum.LoginFailedWrongPassword,
                targetAccountId: null,
                isSuccess: false,
                targetEmail: normalizedEmail,
                reason: "Invalid credentials.",
                cancellationToken: cancellationToken);
            await PublishLoginAttempt(null, normalizedEmail,
                LoginAttemptResult.AccountNotFound,
                note: "Invalid credentials.",
                cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            AppMetrics.AuthLoginTotal.WithLabels("invalid_credentials").Inc(); // #AUTH-78
            return Fail(400, "Email hoặc mật khẩu không chính xác.");
        }

        if (account.LockoutEndAt.HasValue && account.LockoutEndAt.Value > DateTime.UtcNow)
        {
            var minutesLeft = (int)Math.Ceiling((account.LockoutEndAt.Value - DateTime.UtcNow).TotalMinutes);
            await PublishAudit(AuditActionEnum.LoginFailedAccountLocked, account.Id, isSuccess: false,
                targetEmail: account.Email,
                reason: $"Account đang lockout, còn {minutesLeft} phút.",
                cancellationToken: cancellationToken);
            await PublishLoginAttempt(account.Id, account.Email, LoginAttemptResult.AccountLocked,
                note: $"Lockout còn {minutesLeft} phút.",
                cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Fail(423, $"Tài khoản đang bị khóa. Vui lòng thử lại sau {minutesLeft} phút.");
        }

        switch (account.Status)
        {
            case AccountStatusEnum.PendingVerification:
                await PublishAudit(AuditActionEnum.LoginFailedNotVerified, account.Id, false,
                    targetEmail: account.Email, cancellationToken: cancellationToken);
                await PublishLoginAttempt(account.Id, account.Email, LoginAttemptResult.AccountNotVerified,
                    cancellationToken: cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Fail(403, "Tài khoản chưa được xác thực. Vui lòng kiểm tra email.");
            case AccountStatusEnum.Inactive:
                await PublishAudit(AuditActionEnum.LoginFailedAccountInactive, account.Id, false,
                    targetEmail: account.Email, cancellationToken: cancellationToken);
                await PublishLoginAttempt(account.Id, account.Email, LoginAttemptResult.AccountInactive,
                    cancellationToken: cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Fail(403, "Tài khoản đã bị vô hiệu hóa.");
            case AccountStatusEnum.Suspended:
                await PublishAudit(AuditActionEnum.LoginFailedAccountSuspended, account.Id, false,
                    targetEmail: account.Email, cancellationToken: cancellationToken);
                await PublishLoginAttempt(account.Id, account.Email, LoginAttemptResult.AccountSuspended,
                    cancellationToken: cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Fail(403, "Tài khoản đang bị đình chỉ.");
            case AccountStatusEnum.Banned:
                await PublishAudit(AuditActionEnum.LoginFailedAccountBanned, account.Id, false,
                    targetEmail: account.Email, cancellationToken: cancellationToken);
                await PublishLoginAttempt(account.Id, account.Email, LoginAttemptResult.AccountBanned,
                    cancellationToken: cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Fail(403, "Tài khoản đã bị cấm.");
            case AccountStatusEnum.Locked:
                if (!account.LockoutEndAt.HasValue || account.LockoutEndAt.Value <= DateTime.UtcNow)
                {
                    account.Status = AccountStatusEnum.Active;
                    account.LockoutEndAt = null;
                    account.FailedLoginAttempts = 0;
                }
                else
                {
                    await PublishAudit(AuditActionEnum.LoginFailedAccountLocked, account.Id, false,
                        targetEmail: account.Email, cancellationToken: cancellationToken);
                    await PublishLoginAttempt(account.Id, account.Email, LoginAttemptResult.AccountLocked,
                        cancellationToken: cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    return Fail(423, "Tài khoản đang bị khóa tạm thời.");
                }
                break;
        }

        var passwordValid = _passwordHasher.Verify(request.Password, account.PasswordHash);

        if (!passwordValid)
        {
            account.FailedLoginAttempts += 1;
            var wasJustLocked = false;

            if (account.FailedLoginAttempts >= MaxFailedAttempts)
            {
                account.Status = AccountStatusEnum.Locked;
                account.LockoutEndAt = DateTime.UtcNow.AddMinutes(LockoutDurationMinutes);
                wasJustLocked = true;
            }

            _unitOfWork.Accounts.UpdateAsync(account);

            await PublishAudit(AuditActionEnum.LoginFailedWrongPassword, account.Id, false,
                targetEmail: account.Email,
                reason: $"Sai mật khẩu lần {account.FailedLoginAttempts}/{MaxFailedAttempts}.",
                metadata: new Dictionary<string, object?>
                {
                    ["failedAttempts"] = account.FailedLoginAttempts,
                    ["maxAttempts"] = MaxFailedAttempts
                },
                cancellationToken: cancellationToken);

            if (wasJustLocked)
            {
                await PublishAudit(AuditActionEnum.AccountAutoLocked, account.Id, true,
                    targetEmail: account.Email,
                    reason: $"Auto-lock {LockoutDurationMinutes} phút sau {MaxFailedAttempts} lần sai mật khẩu.",
                    metadata: new Dictionary<string, object?>
                    {
                        ["lockoutMinutes"] = LockoutDurationMinutes,
                        ["lockoutEndAt"] = account.LockoutEndAt
                    },
                    cancellationToken: cancellationToken);
            }

            await PublishLoginAttempt(account.Id, account.Email, LoginAttemptResult.WrongPassword,
                note: $"Sai mật khẩu lần {account.FailedLoginAttempts}/{MaxFailedAttempts}." +
                      (wasJustLocked ? " Auto-locked." : ""),
                cancellationToken: cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (wasJustLocked)
                return Fail(423,
                    $"Sai mật khẩu quá {MaxFailedAttempts} lần. Tài khoản bị khóa {LockoutDurationMinutes} phút.");

            var remaining = MaxFailedAttempts - account.FailedLoginAttempts;
            return Fail(400, $"Email hoặc mật khẩu không chính xác. Còn {remaining} lần thử.");
        }

        var (ipAddress, userAgent, deviceId) = ClientInfoHelper.Resolve(_httpContextAccessor?.HttpContext);

        // 2FA enabled → trả challenge token thay vì JWT. KHÔNG reset FailedLoginAttempts ở đây — chỉ reset
        // sau khi verify-2fa thành công (để brute force TOTP cũng tốn quota password).
        // Cũng KHÔNG ghi LoginAttempt success — đợi verify-2fa.
        if (account.TwoFactorEnabled && !string.IsNullOrEmpty(account.TwoFactorSecret))
        {
            var challengeToken = await _challengeStore.CreateAsync(
                account.Id,
                ipAddress ?? string.Empty,
                userAgent ?? string.Empty,
                ChallengeTtl,
                cancellationToken);

            await PublishAudit(AuditActionEnum.LoginPending2FA, account.Id, isSuccess: true,
                targetEmail: account.Email,
                reason: "Password OK, 2FA pending",
                actorAccountIdOverride: account.Id,
                cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new LoginResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Yêu cầu xác thực 2FA. Gửi mã TOTP hoặc backup code qua /api/auth/login/verify-2fa.",
                Data = new LoginResultDto
                {
                    Challenge = new TwoFactorChallengeDto
                    {
                        ChallengeToken = challengeToken,
                        ExpiresInSeconds = (int)ChallengeTtl.TotalSeconds,
                        Methods = new List<string> { "totp", "backupCode" }
                    }
                }
            };
        }

        var (tokens, sessionId) = await _tokenIssuer.IssueAsync(account, ipAddress, userAgent, deviceId, cancellationToken);

        AppMetrics.AuthLoginTotal.WithLabels("success").Inc(); // #AUTH-78

        var roleName = account.Role?.Name ?? string.Empty;
        await PublishAudit(AuditActionEnum.LoginSuccess, account.Id, true,
            targetEmail: account.Email,
            actorAccountIdOverride: account.Id,
            metadata: new Dictionary<string, object?>
            {
                ["role"] = roleName,
                ["sessionId"] = sessionId
            },
            cancellationToken: cancellationToken);

        await PublishLoginAttempt(account.Id, account.Email, LoginAttemptResult.Success,
            cancellationToken: cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new LoginResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đăng nhập thành công.",
            Data = new LoginResultDto { Tokens = tokens }
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

    private Task PublishLoginAttempt(
        Guid? accountId,
        string attemptedEmail,
        LoginAttemptResult result,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        return _publisher.Publish(new LoginAttemptNotification(
            accountId, attemptedEmail, result, Method: "Password", note),
            cancellationToken);
    }

    private static LoginResponse Fail(int statusCode, string message)
    {
        return new LoginResponse
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message,
        };
    }

    // #AUTH-17: delay 100-200ms (RNG-based jitter) trên miss path để time ~= BCrypt verify time.
    private static Task ApplyEnumerationDelay(CancellationToken cancellationToken)
    {
        var jitterMs = RandomNumberGenerator.GetInt32(100, 201);
        return Task.Delay(jitterMs, cancellationToken);
    }
}
