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
using SharedContracts.Events;
using SharedContracts.Interfaces;
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
    private readonly IMessageProducerService _messageProducer;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public LoginCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IAuthTokenIssuer tokenIssuer,
        ITwoFactorChallengeStore challengeStore,
        IPublisher publisher,
        IMessageProducerService messageProducer,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _tokenIssuer = tokenIssuer;
        _challengeStore = challengeStore;
        _publisher = publisher;
        _messageProducer = messageProducer;
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
            return Fail(400, "Incorrect email or password.");
        }

        if (account.LockoutEndAt.HasValue && account.LockoutEndAt.Value > DateTime.UtcNow)
        {
            var minutesLeft = (int)Math.Ceiling((account.LockoutEndAt.Value - DateTime.UtcNow).TotalMinutes);
            await PublishAudit(AuditActionEnum.LoginFailedAccountLocked, account.Id, isSuccess: false,
                targetEmail: account.Email,
                reason: $"Account is in lockout, {minutesLeft} minute(s) remaining.",
                cancellationToken: cancellationToken);
            await PublishLoginAttempt(account.Id, account.Email, LoginAttemptResult.AccountLocked,
                note: $"Lockout {minutesLeft} minute(s) remaining.",
                cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Fail(423, $"Account is locked. Please try again in {minutesLeft} minute(s).");
        }

        switch (account.Status)
        {
            case AccountStatusEnum.PendingVerification:
                await PublishAudit(AuditActionEnum.LoginFailedNotVerified, account.Id, false,
                    targetEmail: account.Email, cancellationToken: cancellationToken);
                await PublishLoginAttempt(account.Id, account.Email, LoginAttemptResult.AccountNotVerified,
                    cancellationToken: cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Fail(403, "Account is not verified yet. Please check your email.");
            case AccountStatusEnum.Inactive:
                await PublishAudit(AuditActionEnum.LoginFailedAccountInactive, account.Id, false,
                    targetEmail: account.Email, cancellationToken: cancellationToken);
                await PublishLoginAttempt(account.Id, account.Email, LoginAttemptResult.AccountInactive,
                    cancellationToken: cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Fail(403, "Account has been deactivated.");
            case AccountStatusEnum.Suspended:
                await PublishAudit(AuditActionEnum.LoginFailedAccountSuspended, account.Id, false,
                    targetEmail: account.Email, cancellationToken: cancellationToken);
                await PublishLoginAttempt(account.Id, account.Email, LoginAttemptResult.AccountSuspended,
                    cancellationToken: cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Fail(403, "Account is suspended.");
            case AccountStatusEnum.Banned:
                await PublishAudit(AuditActionEnum.LoginFailedAccountBanned, account.Id, false,
                    targetEmail: account.Email, cancellationToken: cancellationToken);
                await PublishLoginAttempt(account.Id, account.Email, LoginAttemptResult.AccountBanned,
                    cancellationToken: cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Fail(403, "Account has been banned.");
            case AccountStatusEnum.Locked:
                if (!account.LockoutEndAt.HasValue || account.LockoutEndAt.Value <= DateTime.UtcNow)
                {
                    account.Status = AccountStatusEnum.Active;
                    account.LockoutEndAt = null;
                    account.FailedLoginAttempts = 0;

                    // GH-766 bịt nửa ĐI của cặp Active ↔ Locked (tự khoá có phát event) nhưng bỏ sót
                    // nửa VỀ này. BatteryService.AccountStatusChangedConsumer đặt
                    // IsActive = (NewStatus == 1): tự khoá đẩy IsActive=false, không phát gì ở đây thì
                    // không có gì đưa nó về true — khách gõ sai mật khẩu 5 lần rồi đăng nhập lại bình
                    // thường vẫn bị coi là ngừng hoạt động cho tới khi có người chạy resync thủ công.
                    //
                    // NotificationService KHÔNG bị ảnh hưởng bởi cặp chuyển này (IsNotifiable coi
                    // Locked vẫn là còn nhận thông báo) — đó là lý do trước đây tưởng như không cần.
                    //
                    // Outbox ⇒ nguyên tử với SaveChangesAsync; mọi nhánh thoát phía sau đều gọi nó.
                    await _messageProducer.PublishAsync(new AccountStatusChangedEvent(
                        account.Id,
                        account.Email,
                        (int)AccountStatusEnum.Locked,
                        (int)AccountStatusEnum.Active,
                        "Lockout expired — auto-unlocked on login."), cancellationToken);
                }
                else
                {
                    await PublishAudit(AuditActionEnum.LoginFailedAccountLocked, account.Id, false,
                        targetEmail: account.Email, cancellationToken: cancellationToken);
                    await PublishLoginAttempt(account.Id, account.Email, LoginAttemptResult.AccountLocked,
                        cancellationToken: cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    return Fail(423, "Account is temporarily locked.");
                }
                break;
        }

        var passwordValid = _passwordHasher.Verify(request.Password, account.PasswordHash);

        if (!passwordValid)
        {
            account.FailedLoginAttempts += 1;
            var wasJustLocked = false;
            var previousStatus = account.Status;   // GH-766 — bắt trước khi có thể bị ghi đè thành Locked.

            if (account.FailedLoginAttempts >= MaxFailedAttempts)
            {
                account.Status = AccountStatusEnum.Locked;
                account.LockoutEndAt = DateTime.UtcNow.AddMinutes(LockoutDurationMinutes);
                wasJustLocked = true;
            }

            _unitOfWork.Accounts.UpdateAsync(account);

            await PublishAudit(AuditActionEnum.LoginFailedWrongPassword, account.Id, false,
                targetEmail: account.Email,
                reason: $"Wrong password attempt {account.FailedLoginAttempts}/{MaxFailedAttempts}.",
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
                    reason: $"Auto-locked for {LockoutDurationMinutes} minutes after {MaxFailedAttempts} failed password attempts.",
                    metadata: new Dictionary<string, object?>
                    {
                        ["lockoutMinutes"] = LockoutDurationMinutes,
                        ["lockoutEndAt"] = account.LockoutEndAt
                    },
                    cancellationToken: cancellationToken);

                // GH-766 — tự khoá cũng là một chuyển đổi trạng thái, và là đường DỄ XẢY RA NHẤT
                // (không cần admin thao tác). Không phát event ở đây thì Battery/Ticket vẫn coi
                // tài khoản đang bị brute-force là bình thường. Outbox ⇒ nguyên tử với SaveChanges.
                await _messageProducer.PublishAsync(new AccountStatusChangedEvent(
                    account.Id,
                    account.Email,
                    (int)previousStatus,
                    (int)AccountStatusEnum.Locked,
                    $"Auto-locked after {MaxFailedAttempts} failed password attempts."), cancellationToken);
            }

            await PublishLoginAttempt(account.Id, account.Email, LoginAttemptResult.WrongPassword,
                note: $"Wrong password attempt {account.FailedLoginAttempts}/{MaxFailedAttempts}." +
                      (wasJustLocked ? " Auto-locked." : ""),
                cancellationToken: cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (wasJustLocked)
                return Fail(423,
                    $"Incorrect password entered {MaxFailedAttempts} times. Account locked for {LockoutDurationMinutes} minutes.");

            var remaining = MaxFailedAttempts - account.FailedLoginAttempts;
            return Fail(400, $"Incorrect email or password. {remaining} attempt(s) remaining.");
        }

        var (ipAddress, userAgent, deviceId) = ClientInfoHelper.Resolve(_httpContextAccessor?.HttpContext);

        // 2FA enabled → trả challenge token thay vì JWT. KHÔNG reset FailedLoginAttempts ở đây — chỉ reset
        // sau khi verify-2fa thành công (để brute force TOTP cũng tốn quota password).
        // Cũng KHÔNG ghi LoginAttempt success — đợi verify-2fa.
        if (account.TwoFactorEnabled && !string.IsNullOrEmpty(account.TwoFactorSecret))
        {
            // #AUTH-48: Check trusted device → nếu match (fingerprint + IP prefix), skip challenge issue tokens trực tiếp.
            // KHÔNG match → flow 2FA challenge bình thường.
            var trustedDevice = await FindActiveTrustedDeviceAsync(account.Id, deviceId, userAgent, ipAddress, cancellationToken);
            if (trustedDevice != null)
            {
                trustedDevice.LastUsedAt = DateTime.UtcNow;
                trustedDevice.UsageCount++;
                _unitOfWork.TrustedDevices.UpdateAsync(trustedDevice);

                var (trustedTokens, trustedSessionId) = await _tokenIssuer.IssueAsync(account, ipAddress, userAgent, deviceId, cancellationToken);

                account.FailedLoginAttempts = 0;
                account.LastLoginAt = DateTime.UtcNow;
                account.LastLoginIp = ipAddress;
                _unitOfWork.Accounts.UpdateAsync(account);

                AppMetrics.AuthLoginTotal.WithLabels("success_trusted_device").Inc();
                AppMetrics.Auth2FAChallengeTotal.WithLabels("skipped_trusted_device").Inc();

                await PublishAudit(AuditActionEnum.LoginWithTrustedDevice, account.Id, isSuccess: true,
                    targetEmail: account.Email,
                    actorAccountIdOverride: account.Id,
                    metadata: new Dictionary<string, object?>
                    {
                        ["trustedDeviceId"] = trustedDevice.Id,
                        ["label"] = trustedDevice.Label,
                        ["sessionId"] = trustedSessionId
                    },
                    cancellationToken: cancellationToken);
                await PublishLoginAttempt(account.Id, account.Email, LoginAttemptResult.Success,
                    note: "Trusted device — 2FA skipped",
                    cancellationToken: cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return new LoginResponse
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Message = "Login successful (trusted device).",
                    Data = new LoginResultDto { Tokens = trustedTokens }
                };
            }

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
                Message = "2FA verification required. Send a TOTP code or backup code via /api/auth/login/verify-2fa.",
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
            Message = "Login successful.",
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

    // #AUTH-48: Tìm TrustedDevice active match (account, fingerprint, ipPrefix). null nếu không match.
    // Match rule: fingerprint === device + IP /24 subnet matched + not revoked + ExpiresAt > now.
    private async Task<Domain.Entities.TrustedDevice?> FindActiveTrustedDeviceAsync(
        Guid accountId, string? deviceId, string? userAgent, string? ipAddress, CancellationToken cancellationToken)
    {
        var fingerprint = TrustedDeviceFingerprintHelper.ComputeFingerprint(deviceId, userAgent);
        var ipPrefix = TrustedDeviceFingerprintHelper.ComputeIpPrefix(ipAddress);
        if (fingerprint == null || ipPrefix == null)
            return null;

        var now = DateTime.UtcNow;
        return await _unitOfWork.TrustedDevices
            .GetAllAsync()
            .FirstOrDefaultAsync(td =>
                td.AccountId == accountId
                && td.DeviceFingerprintHash == fingerprint
                && td.IpPrefix == ipPrefix
                && td.RevokedAt == null
                && td.ExpiresAt > now
                && !td.IsDeleted, cancellationToken);
    }
}
