using AuthService.Application.Authorization;
using AuthService.Application.CQRS.Command.Auth;
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
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Auth;

public class GoogleAuthCommandHandler : IRequestHandler<GoogleAuthCommand, LoginResponse>
{
    private const int RefreshTokenExpirationDays = 7;
    private const string ProviderName = "Google";
    private static readonly Guid CustomerRoleId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IJwtHelper _jwtHelper;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IGoogleOAuthHelper _googleOAuthHelper;
    private readonly IMessageProducerService _messageProducer;
    private readonly IPublisher _publisher;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public GoogleAuthCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IJwtHelper jwtHelper,
        IPasswordHasher passwordHasher,
        IGoogleOAuthHelper googleOAuthHelper,
        IMessageProducerService messageProducer,
        IPublisher publisher,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _unitOfWork = unitOfWork;
        _jwtHelper = jwtHelper;
        _passwordHasher = passwordHasher;
        _googleOAuthHelper = googleOAuthHelper;
        _messageProducer = messageProducer;
        _publisher = publisher;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<LoginResponse> Handle(GoogleAuthCommand request, CancellationToken cancellationToken)
    {
        var googleUser = await _googleOAuthHelper.ValidateAsync(request.IdToken, cancellationToken);
        if (googleUser == null || string.IsNullOrWhiteSpace(googleUser.Email))
            return Fail(401, "Google ID token không hợp lệ.");

        if (!googleUser.EmailVerified)
            return Fail(401, "Email Google chưa được xác thực.");

        var normalizedEmail = googleUser.Email.Trim().ToLowerInvariant();

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .Include(a => a.AccountRoles.Where(ar => ar.IsActive && !ar.IsDeleted))
                .ThenInclude(ar => ar.Role)
            .FirstOrDefaultAsync(a => a.Email.ToLower() == normalizedEmail, cancellationToken);

        var (ipAddress, userAgent, deviceId) = ClientInfoHelper.Resolve(_httpContextAccessor?.HttpContext);
        var isNewAccount = account == null;

        if (isNewAccount)
        {
            account = new Domain.Entities.Account
            {
                Id = Guid.NewGuid(),
                Email = normalizedEmail,
                PasswordHash = _passwordHasher.Hash(Guid.NewGuid().ToString("N")),
                FullName = string.IsNullOrWhiteSpace(googleUser.Name) ? normalizedEmail : googleUser.Name,
                AvatarUrl = null,
                EmailConfirmed = true,
                PhoneConfirmed = false,
                TwoFactorEnabled = false,
                Status = AccountStatusEnum.Active,
                GoogleId = googleUser.Subject,
                Provider = ProviderName,
                LastLoginAt = DateTime.UtcNow,
                LastLoginIp = ipAddress,
                FailedLoginAttempts = 0,
                LockoutEndAt = null
            };
            await _unitOfWork.Accounts.AddAsync(account);
            await _unitOfWork.AccountRoles.AddAsync(new AccountRole
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                RoleId = CustomerRoleId,
                AssignedAt = DateTime.UtcNow,
                IsActive = true
            });
        }
        else
        {
            if (account!.IsDeleted || account.Status == AccountStatusEnum.Banned || account.Status == AccountStatusEnum.Suspended)
                return Fail(403, "Tài khoản không khả dụng.");

            if (account.Status == AccountStatusEnum.PendingVerification)
                return Fail(409, "Email đã đăng ký nhưng chưa xác thực. Vui lòng verify OTP trước.");

            if (string.IsNullOrEmpty(account.GoogleId))
            {
                if (!account.EmailConfirmed)
                    return Fail(409, "Vui lòng verify email trước khi liên kết Google.");
                account.GoogleId = googleUser.Subject;
                account.Provider = ProviderName;
            }
            else if (account.GoogleId != googleUser.Subject)
            {
                return Fail(409, "Tài khoản này đã liên kết với 1 Google account khác.");
            }

            account.LastLoginAt = DateTime.UtcNow;
            account.LastLoginIp = ipAddress;
            account.FailedLoginAttempts = 0;
            account.LockoutEndAt = null;
            if (account.Status == AccountStatusEnum.Locked)
                account.Status = AccountStatusEnum.Active;
            _unitOfWork.Accounts.UpdateAsync(account);
        }

        // Lưu ý: AccountRole mới vừa AddAsync ở trên có Role nav = null (chỉ set RoleId).
        // EF Change Tracker auto-fixup đẩy entry đó vào account.AccountRoles → cần filter ar.Role != null.
        var roleNames = account.AccountRoles
            .Where(ar => ar.IsActive
                         && ar.Role != null
                         && (ar.ExpiredAt == null || ar.ExpiredAt > DateTime.UtcNow))
            .Select(ar => ar.Role!.Name)
            .ToList();
        if (!roleNames.Contains("Customer"))
            roleNames.Add("Customer");

        var permissionCodes = await PermissionResolver.GetPermissionCodesAsync(_unitOfWork, account.Id, cancellationToken);
        var accessToken = await _jwtHelper.GenerateAccessToken(account, roleNames, permissionCodes);
        var refreshTokenValue = _jwtHelper.GenerateRefreshToken();

        await _unitOfWork.RefreshTokens.AddAsync(new RefreshToken
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
        });

        if (isNewAccount)
        {
            await _messageProducer.PublishAsync(new AccountActivatedEvent(
                account.Id,
                account.Email,
                account.FullName,
                account.PhoneNumber,
                roleNames,
                CreationSource: "GoogleOAuth"), cancellationToken);
        }

        await _publisher.Publish(new SessionCreatedNotification(account.Id), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new LoginResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đăng nhập Google thành công.",
            Data = new TokenDTO
            {
                AccessToken = accessToken,
                RefreshToken = refreshTokenValue
            }
        };
    }

    private static LoginResponse Fail(int statusCode, string message, string field = "Auth") => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
        ListErrors = new List<Errors> { new Errors { Field = field, Detail = message } }
    };
}
