using AuthService.Application.Authorization;
using AuthService.Application.Configuration;
using AuthService.Application.CQRS.Notification.Session;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Infrastructure.Implements.Services;

public class AuthTokenIssuer : IAuthTokenIssuer
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IJwtHelper _jwtHelper;
    private readonly IPublisher _publisher;
    private readonly IMessageProducerService _messageProducer;
    private readonly JwtSettingsOptions _jwtSettings;

    public AuthTokenIssuer(
        IAuthUnitOfWork unitOfWork,
        IJwtHelper jwtHelper,
        IPublisher publisher,
        IMessageProducerService messageProducer,
        IOptions<JwtSettingsOptions> jwtSettings)
    {
        _unitOfWork = unitOfWork;
        _jwtHelper = jwtHelper;
        _publisher = publisher;
        _messageProducer = messageProducer;
        _jwtSettings = jwtSettings.Value;
    }

    public async Task<(TokenDTO Tokens, Guid SessionId)> IssueAsync(
        Account account,
        string? ipAddress,
        string? userAgent,
        string? deviceId,
        CancellationToken cancellationToken = default)
    {
        // #AUTH-68: set LastLoginAt/LastLoginIp ở 1 nơi duy nhất (AuthTokenIssuer) — cover cả login
        // password flow, login 2FA flow, AcceptInvite, GoogleAuth. Trước đây spec lo ngại sai lệch
        // giữa LoginCommandHandler (không set) và RefreshTokenCommandHandler (có set). Giờ cả 2
        // đều delegate vào IssueAsync nên consistent.
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
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            // #AUTH-01: chỉ lưu hash trong DB; plaintext trả về client qua TokenDTO.
            Token = RefreshTokenHasher.Hash(refreshTokenValue),
            IssuedAt = nowUtc,
            // #AUTH-28: first issuance — OriginalIssuedAt = IssuedAt. Khi rotate, copy giá trị này.
            OriginalIssuedAt = nowUtc,
            ExpiredAt = nowUtc.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            Status = RefreshTokenStatus.Active,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            DeviceId = deviceId
        };

        await _unitOfWork.RefreshTokens.AddAsync(refreshToken);

        await _publisher.Publish(new SessionCreatedNotification(account.Id), cancellationToken);

        // #AUTH-52: detect suspicious login dựa trên IP/UA chưa từng thấy trong các session active+used trước đây.
        await DetectAndPublishSuspiciousLoginAsync(account, ipAddress, userAgent, cancellationToken);

        return (new TokenDTO { AccessToken = accessToken, RefreshToken = refreshTokenValue }, refreshToken.Id);
    }

    private async Task DetectAndPublishSuspiciousLoginAsync(
        Account account, string? ipAddress, string? userAgent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(ipAddress) && string.IsNullOrEmpty(userAgent))
            return;

        // Đọc các session đã có (KHÔNG kể session mới vừa Add). Limit 50 để query không tốn nhiều.
        var historical = await _unitOfWork.RefreshTokens.GetAllAsync()
            .AsNoTracking()
            .Where(rt => rt.AccountId == account.Id)
            .OrderByDescending(rt => rt.IssuedAt)
            .Take(50)
            .Select(rt => new { rt.IpAddress, rt.UserAgent })
            .ToListAsync(cancellationToken);

        if (historical.Count == 0)
            return; // first login → không phải suspicious.

        var newIp = !string.IsNullOrEmpty(ipAddress) &&
                    !historical.Any(h => string.Equals(h.IpAddress, ipAddress, StringComparison.OrdinalIgnoreCase));
        var newUa = !string.IsNullOrEmpty(userAgent) &&
                    !historical.Any(h => string.Equals(h.UserAgent, userAgent, StringComparison.Ordinal));

        if (!newIp && !newUa)
            return;

        var reason = (newIp, newUa) switch
        {
            (true, true) => "new_ip_and_user_agent",
            (true, false) => "new_ip",
            (false, true) => "new_user_agent",
            _ => "unknown"
        };

        await _messageProducer.PublishAsync(new SuspiciousLoginDetectedEvent(
            account.Id, account.Email, ipAddress, userAgent, reason, DateTime.UtcNow), cancellationToken);
    }
}
