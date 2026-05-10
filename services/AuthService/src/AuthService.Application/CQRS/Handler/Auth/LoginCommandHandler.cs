using AuthService.Application.CQRS.Command.Auth;
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
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public LoginCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IJwtHelper jwtHelper,
        IPasswordHasher passwordHasher,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _unitOfWork = unitOfWork;
        _jwtHelper = jwtHelper;
        _passwordHasher = passwordHasher;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .Include(a => a.AccountRoles.Where(ar => ar.IsActive && !ar.IsDeleted))
                .ThenInclude(ar => ar.Role)
            .FirstOrDefaultAsync(a => a.Email.ToLower() == normalizedEmail, cancellationToken);

        if (account == null)
            return Fail(401, "Email hoặc mật khẩu không chính xác.");

        if (account.LockoutEndAt.HasValue && account.LockoutEndAt.Value > DateTime.UtcNow)
        {
            var minutesLeft = (int)Math.Ceiling((account.LockoutEndAt.Value - DateTime.UtcNow).TotalMinutes);
            return Fail(423, $"Tài khoản đang bị khóa. Vui lòng thử lại sau {minutesLeft} phút.");
        }

        switch (account.Status)
        {
            case AccountStatusEnum.PendingVerification:
                return Fail(403, "Tài khoản chưa được xác thực. Vui lòng kiểm tra email.");
            case AccountStatusEnum.Inactive:
                return Fail(403, "Tài khoản đã bị vô hiệu hóa.");
            case AccountStatusEnum.Suspended:
                return Fail(403, "Tài khoản đang bị đình chỉ.");
            case AccountStatusEnum.Banned:
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
                    return Fail(423, "Tài khoản đang bị khóa tạm thời.");
                }
                break;
        }

        var passwordValid = _passwordHasher.Verify(request.Password, account.PasswordHash);

        if (!passwordValid)
        {
            account.FailedLoginAttempts += 1;

            if (account.FailedLoginAttempts >= MaxFailedAttempts)
            {
                account.Status = AccountStatusEnum.Locked;
                account.LockoutEndAt = DateTime.UtcNow.AddMinutes(LockoutDurationMinutes);
            }

            _unitOfWork.Accounts.UpdateAsync(account);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (account.Status == AccountStatusEnum.Locked)
            {
                return Fail(423,
                    $"Sai mật khẩu quá {MaxFailedAttempts} lần. Tài khoản bị khóa {LockoutDurationMinutes} phút.");
            }

            var remaining = MaxFailedAttempts - account.FailedLoginAttempts;
            return Fail(401, $"Email hoặc mật khẩu không chính xác. Còn {remaining} lần thử.");
        }

        var (ipAddress, userAgent, deviceId) = ClientInfoHelper.Resolve(_httpContextAccessor?.HttpContext);

        account.FailedLoginAttempts = 0;
        account.LockoutEndAt = null;
        account.LastLoginAt = DateTime.UtcNow;
        account.LastLoginIp = ipAddress;
        _unitOfWork.Accounts.UpdateAsync(account);

        var roleNames = account.AccountRoles
            .Where(ar => ar.IsActive && (ar.ExpiredAt == null || ar.ExpiredAt > DateTime.UtcNow))
            .Select(ar => ar.Role.Name)
            .ToList();

        var accessToken = await _jwtHelper.GenerateAccessToken(account, roleNames);
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
