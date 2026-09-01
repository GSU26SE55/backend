using AuthService.Application.Authorization;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Notification.Audit;
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

public class AcceptInviteCommandHandler : IRequestHandler<AcceptInviteCommand, LoginResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtHelper _jwtHelper;
    private readonly IMessageProducerService _messageProducer;
    private readonly IPublisher _publisher;
    private readonly AuthService.Application.Configuration.JwtSettingsOptions _jwtSettings;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public AcceptInviteCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IJwtHelper jwtHelper,
        IMessageProducerService messageProducer,
        IPublisher publisher,
        Microsoft.Extensions.Options.IOptions<AuthService.Application.Configuration.JwtSettingsOptions> jwtSettings,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtHelper = jwtHelper;
        _messageProducer = messageProducer;
        _publisher = publisher;
        _jwtSettings = jwtSettings.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<LoginResponse> Handle(AcceptInviteCommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .Where(a => !a.IsDeleted)
            .Include(a => a.Role)
            .FirstOrDefaultAsync(a => a.InvitationToken == request.InvitationToken, cancellationToken);

        // #38 QA solars.io.vn 2026-08-29: 401 ở 2 nhánh này từng bị axios.ts coi là hết phiên
        // (mọi 401 != TOKEN_EXPIRED ⇒ auto-logout) ⇒ nhận lời mời với token sai/hết hạn là bị
        // văng thẳng về /login mất cả luồng, không hiện thông báo gì.
        if (account == null)
            return Fail(400, "Invalid or already used invitation token.");

        // #AUTH-26: reject nếu InvitationExpiredAt null (invite không bao giờ hết hạn) hoặc đã quá hạn.
        // Dùng <= cho on-exact-expiry edge case, đồng bộ với #AUTH-27.
        if (!account.InvitationExpiredAt.HasValue || account.InvitationExpiredAt.Value <= DateTime.UtcNow)
            return Fail(400, "Invitation token has expired. Please ask an admin to resend the invite.");

        if (account.Status != AccountStatusEnum.PendingVerification)
            return Fail(409, "Account has already been activated.");

        var (ipAddress, userAgent, deviceId) = ClientInfoHelper.Resolve(_httpContextAccessor?.HttpContext);

        // Activate account
        account.PasswordHash = _passwordHasher.Hash(request.Password);
        account.EmailConfirmed = true;
        account.Status = AccountStatusEnum.Active;
        account.InvitationToken = null;
        account.InvitationExpiredAt = null;
        account.FailedLoginAttempts = 0;
        account.LockoutEndAt = null;
        account.LastLoginAt = DateTime.UtcNow;
        account.LastLoginIp = ipAddress;
        _unitOfWork.Accounts.UpdateAsync(account);

        var roleName = account.Role?.Name ?? string.Empty;

        var permissionCodes = await PermissionResolver.GetPermissionCodesAsync(_unitOfWork, account.Id, cancellationToken);
        var accessToken = await _jwtHelper.GenerateAccessToken(account, roleName, permissionCodes);
        var refreshTokenValue = _jwtHelper.GenerateRefreshToken();

        var nowUtc = DateTime.UtcNow;
        await _unitOfWork.RefreshTokens.AddAsync(new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            // #AUTH-01: lưu hash, return plaintext qua TokenDTO.
            Token = RefreshTokenHasher.Hash(refreshTokenValue),
            IssuedAt = nowUtc,
            OriginalIssuedAt = nowUtc, // #AUTH-28
            ExpiredAt = nowUtc.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            Status = RefreshTokenStatus.Active,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            DeviceId = deviceId
        });

        await _publisher.Publish(new SessionCreatedNotification(account.Id), cancellationToken);

        // Outbox: account đã trở thành Active → publish AccountActivatedEvent cho subscribers.
        await _messageProducer.PublishAsync(new AccountActivatedEvent(
            account.Id,
            account.Email,
            account.FullName,
            account.PhoneNumber,
            roleName,
            CreationSource: "AdminInvite"), cancellationToken);

        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.AccountInviteAccepted, account.Id, IsSuccess: true,
            TargetEmail: account.Email,
            ActorAccountIdOverride: account.Id), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new LoginResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Account activated and logged in successfully.",
            Data = new LoginResultDto
            {
                Tokens = new TokenDTO
                {
                    AccessToken = accessToken,
                    RefreshToken = refreshTokenValue
                }
            }
        };
    }

    private static LoginResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };
}
