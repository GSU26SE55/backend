using AuthService.Application.DTOs.Response.Auth;
using AuthService.Domain.Entities;

namespace AuthService.Application.Interfaces.Services;

/// <summary>
/// Issuance helper dùng chung cho Login (path không 2FA) và Verify2FALogin (path 2FA).
/// Sinh access token + refresh token, lưu refresh token vào DB (KHÔNG SaveChanges — caller commit chung),
/// publish <c>SessionCreatedNotification</c> để enforce concurrent session limit.
/// </summary>
public interface IAuthTokenIssuer
{
    /// <summary>
    /// Issue access + refresh token. Updates <paramref name="account"/> (LastLogin*) và Adds refresh token vào UnitOfWork.
    /// </summary>
    /// <returns>Tuple of (TokenDTO, refresh token Id) — caller dùng id để audit.</returns>
    Task<(TokenDTO Tokens, Guid SessionId)> IssueAsync(
        Account account,
        string? ipAddress,
        string? userAgent,
        string? deviceId,
        CancellationToken cancellationToken = default);
}
