using AuthService.Domain.Entities;

namespace AuthService.Application.Interfaces.Helpers;

public interface IJwtHelper
{
    /// <summary>
    /// Sinh access token chứa claims: NameIdentifier, AccountId, Email, FullName, Role (1), permission(s).
    /// Quan hệ Role ↔ Account là 1-N — mỗi account chỉ có duy nhất 1 role nên claim Role là single value.
    /// </summary>
    /// <param name="account">Account entity.</param>
    /// <param name="role">Role name (vd: "Admin", "Customer"). Có thể empty nếu account chưa gán role hợp lệ.</param>
    /// <param name="permissions">Permission code list (vd: ["battery.view", "ticket.assign"]). Có thể null/empty.</param>
    Task<string> GenerateAccessToken(Account account, string role, IEnumerable<string>? permissions = null);

    string GenerateRefreshToken();
    bool IsTokenValid(string token);
    DateTime ConvertUnixTimeToDateTime(long utcExpiredDate);
    (bool, string?) ValidateToken(string accessToken);

    /// <summary>Sinh JWT short-lived dùng riêng cho luồng reset password (claim purpose=password-reset).</summary>
    string GenerateResetToken(Guid accountId, string email, int expiresInMinutes);

    /// <summary>Validate reset token; trả về AccountId nếu hợp lệ, ngược lại trả về error message.</summary>
    (Guid? accountId, string? errorMessage) ValidateResetToken(string token);

    /// <summary>
    /// #AUTH-06: Validate reset token + trả jti + exp để consumer enforce single-use (Redis SET NX).
    /// </summary>
    (Guid? accountId, string? jti, DateTime? expiresAtUtc, string? errorMessage) ValidateResetTokenDetailed(string token);
}
