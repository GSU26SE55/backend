namespace AuthService.Application.DTOs.Response.Auth;

/// <summary>
/// Response sub-DTO khi <c>POST /api/auth/login</c> trả vào account đang bật 2FA.
/// Client tiếp tục gọi <c>POST /api/auth/login/verify-2fa</c> với <see cref="ChallengeToken"/> + TOTP/backup code.
/// </summary>
public class TwoFactorChallengeDto
{
    public string ChallengeToken { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; }
    /// <summary>Phương thức có thể dùng: ["totp", "backupCode"].</summary>
    public List<string> Methods { get; set; } = new();
}
