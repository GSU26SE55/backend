namespace AuthService.Application.DTOs.Response.Auth;

/// <summary>
/// Response của <c>POST /api/accounts/me/2fa/init</c>. Client dùng <see cref="OtpAuthUri"/> để render QR code.
/// 2FA CHƯA được bật ở bước này — phải gọi tiếp <c>POST /api/accounts/me/2fa/confirm</c> với <see cref="PendingToken"/>.
/// </summary>
public class TwoFactorSetupDto
{
    public string Secret { get; set; } = string.Empty;
    public string OtpAuthUri { get; set; } = string.Empty;
    /// <summary>Token gắn với pending state trong Redis (TTL 10’) — gửi lại khi confirm.</summary>
    public string PendingToken { get; set; } = string.Empty;
}
