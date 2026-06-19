namespace AuthService.Application.Configuration;

/// <summary>
/// Config bảo mật cho refresh token flow. Bind từ appsettings <c>AuthSecurity</c> section.
/// </summary>
public class AuthSecurityOptions
{
    public const string SectionName = "AuthSecurity";

    /// <summary>
    /// #AUTH-12: nếu <c>true</c>, refresh token chỉ chấp nhận khi IP + User-Agent của request
    /// khớp với giá trị lúc cấp token. Mismatch → revoke token + fail 401.
    /// Default <c>false</c> để không break flow mobile chuyển mạng. Bật khi production hardening.
    /// </summary>
    public bool EnforceDeviceBinding { get; set; } = false;

    // #AUTH-32: RefreshTokenExpirationDays MOVED sang JwtSettingsOptions để bind đúng section
    // "JwtSettings" theo spec (đồng bộ với AccessTokenExpirationMinutes ở cùng section).
}
