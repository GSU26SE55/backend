using AuthService.Application.Authorization;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.CQRS.Notification.Login;
using AuthService.Application.CQRS.Notification.Session;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Auth;

public class LoginCommandHandler : IRequestHandler<LoginCommand, LoginResponse>
{
    private const int MaxFailedAttempts = 5;
    private const int LockoutDurationMinutes = 15;
    private const int RefreshTokenExpirationDays = 7;

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IJwtHelper _jwtHelper;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPublisher _publisher;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public LoginCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IJwtHelper jwtHelper,
        IPasswordHasher passwordHasher,
        IPublisher publisher,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _unitOfWork = unitOfWork;
        _jwtHelper = jwtHelper;
        _passwordHasher = passwordHasher;
        _publisher = publisher;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .Include(a => a.Role)
            .FirstOrDefaultAsync(a => a.Email.ToLower() == normalizedEmail && !a.IsDeleted, cancellationToken);

        if (account == null)
        {
            await PublishAudit(AuditActionEnum.LoginFailedWrongPassword,
                targetAccountId: null,
                isSuccess: false,
                targetEmail: normalizedEmail,
                reason: "Email không tồn tại trong hệ thống.",
                cancellationToken: cancellationToken);
            await PublishLoginAttempt(null, normalizedEmail,
                LoginAttemptResult.AccountNotFound,
                note: "Email không tồn tại.",
                cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
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

        account.FailedLoginAttempts = 0;
        account.LockoutEndAt = null;
        account.LastLoginAt = DateTime.UtcNow;
        account.LastLoginIp = ipAddress;
        _unitOfWork.Accounts.UpdateAsync(account);

        var roleName = account.Role?.Name ?? string.Empty;

        var permissionCodes = await PermissionResolver.GetPermissionCodesAsync(_unitOfWork, account.Id, cancellationToken);
        var accessToken = await _jwtHelper.GenerateAccessToken(account, roleName, permissionCodes);
        var refreshTokenValue = _jwtHelper.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Token = refreshTokenValue,
            IssuedAt = DateTime.UtcNow,
            ExpiredAt = DateTime.UtcNow.AddDays(RefreshTokenExpirationDays),
            Status = RefreshTokenStatus.Active,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            DeviceId = deviceId
        };

        await _unitOfWork.RefreshTokens.AddAsync(refreshToken);

        // Enforce concurrent session limit (FIFO revoke oldest if exceeded).
        await _publisher.Publish(new SessionCreatedNotification(account.Id), cancellationToken);

        await PublishAudit(AuditActionEnum.LoginSuccess, account.Id, true,
            targetEmail: account.Email,
            actorAccountIdOverride: account.Id,
            metadata: new Dictionary<string, object?>
            {
                ["role"] = roleName,
                ["sessionId"] = refreshToken.Id
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
            Data = new TokenDTO
            {
                AccessToken = accessToken,
                RefreshToken = refreshTokenValue
            }
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

    private static LoginResponse Fail(int statusCode, string message, string field = "Auth")
    {
        return new LoginResponse
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message,
            ListErrors = new List<Errors>
            {
                new Errors { Field = field, Detail = message }
            }
        };
    }
}
