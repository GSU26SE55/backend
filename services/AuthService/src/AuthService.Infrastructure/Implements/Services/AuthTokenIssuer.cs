using AuthService.Application.Authorization;
using AuthService.Application.CQRS.Notification.Session;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using MediatR;

namespace AuthService.Infrastructure.Implements.Services;

public class AuthTokenIssuer : IAuthTokenIssuer
{
    private const int RefreshTokenExpirationDays = 7;

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IJwtHelper _jwtHelper;
    private readonly IPublisher _publisher;

    public AuthTokenIssuer(IAuthUnitOfWork unitOfWork, IJwtHelper jwtHelper, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _jwtHelper = jwtHelper;
        _publisher = publisher;
    }

    public async Task<(TokenDTO Tokens, Guid SessionId)> IssueAsync(
        Account account,
        string? ipAddress,
        string? userAgent,
        string? deviceId,
        CancellationToken cancellationToken = default)
    {
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

        await _publisher.Publish(new SessionCreatedNotification(account.Id), cancellationToken);

        return (new TokenDTO { AccessToken = accessToken, RefreshToken = refreshTokenValue }, refreshToken.Id);
    }
}
