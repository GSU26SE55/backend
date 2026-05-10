namespace AuthService.Application.Interfaces.Helpers;

public interface IGoogleOAuthHelper
{
    /// <summary>
    /// Validate Google ID token (luồng SPA/mobile gửi idToken trực tiếp).
    /// Trả về thông tin user nếu hợp lệ; null nếu invalid/expired.
    /// </summary>
    Task<GoogleUserInfo?> ValidateAsync(string idToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Build URL chuyển hướng người dùng tới trang chọn account của Google.
    /// </summary>
    /// <param name="state">Random nonce để chống CSRF — phải verify ở callback.</param>
    /// <param name="redirectUri">Callback URL phải khớp với cấu hình Google Console.</param>
    string BuildAuthorizationUrl(string state, string redirectUri);

    /// <summary>
    /// Exchange authorization code (Google trả về callback) lấy id_token.
    /// Trả về raw idToken string; sau đó gọi ValidateAsync để parse user info.
    /// Null nếu exchange fail hoặc Google trả lỗi.
    /// </summary>
    Task<string?> ExchangeCodeForIdTokenAsync(string code, string redirectUri, CancellationToken cancellationToken = default);
}

public class GoogleUserInfo
{
    public string Subject { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
    public string? Name { get; set; }
    public string? Picture { get; set; }
}
