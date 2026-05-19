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
            .Include(a => a.Role)
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Email.ToLower() == normalizedEmail && !a.IsDeleted, cancellationToken);

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
                LockoutEndAt = null,
                RoleId = CustomerRoleId,
                RoleAssignedAt = DateTime.UtcNow
            };
            await _unitOfWork.Accounts.AddAsync(account);
            account.Profile = new AccountProfile
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                ExternalAvatarUrl = NormalizeGooglePicture(googleUser.Picture),
                AvatarSource = string.IsNullOrWhiteSpace(googleUser.Picture)
                    ? AvatarSourceEnum.None
                    : AvatarSourceEnum.Google
            };
            await _unitOfWork.AccountProfiles.AddAsync(account.Profile);
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

            await UpsertGoogleAvatarProfileAsync(account, googleUser.Picture);
            _unitOfWork.Accounts.UpdateAsync(account);
        }

        // Account mới vừa add — Role nav có thể null; nếu null fallback "Customer" (đã set RoleId=CustomerRoleId ở trên).
        var roleName = account.Role?.Name
                       ?? (account.RoleId == CustomerRoleId ? "Customer" : string.Empty);

        var permissionCodes = await PermissionResolver.GetPermissionCodesAsync(_unitOfWork, account.Id, cancellationToken);
        var accessToken = await _jwtHelper.GenerateAccessToken(account, roleName, permissionCodes);
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
                roleName,
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

    private async Task UpsertGoogleAvatarProfileAsync(Domain.Entities.Account account, string? pictureUrl)
    {
        var normalizedPicture = NormalizeGooglePicture(pictureUrl);
        if (normalizedPicture is null)
            return;

        var profile = account.Profile ?? new AccountProfile
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id
        };

        profile.ExternalAvatarUrl = normalizedPicture;
        if (profile.AvatarFileId is null)
            profile.AvatarSource = AvatarSourceEnum.Google;

        if (account.Profile is null)
        {
            account.Profile = profile;
            await _unitOfWork.AccountProfiles.AddAsync(profile);
        }
        else
        {
            _unitOfWork.AccountProfiles.UpdateAsync(profile);
        }
    }

    private static string? NormalizeGooglePicture(string? pictureUrl)
    {
        return string.IsNullOrWhiteSpace(pictureUrl) ? null : pictureUrl.Trim();
    }
}
