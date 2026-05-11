using AuthService.Domain.Entities;

namespace AuthService.Application.Interfaces.Helpers;

public interface IJwtHelper
{
    /// <summary>
    /// Sinh access token chứa claims: NameIdentifier, AccountId, Email, FullName, role(s), permission(s).
    /// </summary>
    /// <param name="account">Account entity.</param>
    /// <param name="roles">Role name list.</param>
    /// <param name="permissions">Permission code list (vd: ["battery.view", "ticket.assign"]). Có thể null/empty.</param>
    Task<string> GenerateAccessToken(Account account, IEnumerable<string> roles, IEnumerable<string>? permissions = null);

    string GenerateRefreshToken();
    bool IsTokenValid(string token);
    DateTime ConvertUnixTimeToDateTime(long utcExpiredDate);
    (bool, string?) ValidateToken(string accessToken);

    /// <summary>Sinh JWT short-lived dùng riêng cho luồng reset password (claim purpose=password-reset).</summary>
    string GenerateResetToken(Guid accountId, string email, int expiresInMinutes);

    /// <summary>Validate reset token; trả về AccountId nếu hợp lệ, ngược lại trả về error message.</summary>
    (Guid? accountId, string? errorMessage) ValidateResetToken(string token);
}
