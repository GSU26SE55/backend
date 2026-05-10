using AuthService.Domain.Entities;

namespace AuthService.Application.Interfaces.Helpers;

public interface IJwtHelper
{
    Task<string> GenerateAccessToken(Account account, IEnumerable<string> roles);
    string GenerateRefreshToken();
    bool IsTokenValid(string token);
    DateTime ConvertUnixTimeToDateTime(long utcExpiredDate);
    (bool, string?) ValidateToken(string accessToken);

    /// <summary>Sinh JWT short-lived dùng riêng cho luồng reset password (claim purpose=password-reset).</summary>
    string GenerateResetToken(Guid accountId, string email, int expiresInMinutes);

    /// <summary>Validate reset token; trả về AccountId nếu hợp lệ, ngược lại trả về error message.</summary>
    (Guid? accountId, string? errorMessage) ValidateResetToken(string token);
}
