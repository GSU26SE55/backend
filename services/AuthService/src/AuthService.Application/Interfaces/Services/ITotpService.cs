namespace AuthService.Application.Interfaces.Services;

/// <summary>
/// Time-based One-Time Password (RFC 6238) service.
/// Implementation phải dùng SHA1, 6 digits, period 30s — chuẩn tương thích Google/Microsoft Authenticator.
/// </summary>
public interface ITotpService
{
    /// <summary>Sinh secret base32 ngẫu nhiên (mặc định 20 bytes — chuẩn Google Authenticator).</summary>
    string GenerateSecret();

    /// <summary>
    /// Build <c>otpauth://totp/</c> URI để client render QR.
    /// </summary>
    /// <param name="secret">Base32 secret từ <see cref="GenerateSecret"/>.</param>
    /// <param name="accountLabel">Label hiển thị trong authenticator (vd: email user).</param>
    /// <param name="issuer">Tên issuer (vd: "GSU26SE55 Auth").</param>
    string BuildOtpAuthUri(string secret, string accountLabel, string issuer);

    /// <summary>
    /// Verify mã 6 số. <paramref name="allowedDriftSteps"/>=1 cho phép code của period trước/sau (tổng 90s window) — bù clock skew.
    /// </summary>
    bool VerifyCode(string secret, string code, int allowedDriftSteps = 1);
}
