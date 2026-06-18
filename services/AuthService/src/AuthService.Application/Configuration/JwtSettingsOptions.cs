using System.ComponentModel.DataAnnotations;

namespace AuthService.Application.Configuration;

/// <summary>
/// #AUTH-32 + #AUTH-66: bind từ section <c>JwtSettings</c>. Tất cả field critical đều
/// <see cref="RequiredAttribute"/>; deploy thiếu config sẽ fail fast lúc app startup thay vì
/// throw runtime khi gọi GenerateAccessToken().
/// </summary>
public class JwtSettingsOptions
{
    public const string SectionName = "JwtSettings";

    /// <summary>HMAC secret key cho sign JWT. PHẢI ≥ 32 ký tự (256 bit) cho HS256.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "JwtSettings:SecretKey is required.")]
    [MinLength(32, ErrorMessage = "JwtSettings:SecretKey must be at least 32 characters for HS256.")]
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Issuer claim (iss). PHẢI khớp với validation lúc verify token.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "JwtSettings:Issuer is required.")]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Audience claim (aud). PHẢI khớp với validation lúc verify token.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "JwtSettings:Audience is required.")]
    public string Audience { get; set; } = string.Empty;

    /// <summary>TTL access token (phút). Default 60.</summary>
    [Range(1, 1440, ErrorMessage = "JwtSettings:AccessTokenExpirationMinutes must be between 1 and 1440.")]
    public int AccessTokenExpirationMinutes { get; set; } = 60;

    /// <summary>TTL refresh token (ngày). Default 7. Khi rotate (xem #AUTH-28), TTL anchor vào OriginalIssuedAt.</summary>
    [Range(1, 90, ErrorMessage = "JwtSettings:RefreshTokenExpirationDays must be between 1 and 90.")]
    public int RefreshTokenExpirationDays { get; set; } = 7;

    /// <summary>
    /// #AUTH-59: Key ID hiện tại (đi vào JWT header `kid`). Default "v1".
    /// Đổi giá trị này khi rotate sang SecretKey mới — token mới có kid="v2", token cũ có kid="v1"
    /// (validate được nhờ <see cref="PreviousSecretKey"/>).
    /// </summary>
    public string SigningKeyId { get; set; } = "v1";

    /// <summary>
    /// #AUTH-59: Secret key cũ. Khi rotate, set giá trị cũ vào đây để validate token đang lưu hành
    /// vẫn pass cho đến khi tự expire. Null = không có key cũ (single-key mode).
    /// </summary>
    public string? PreviousSecretKey { get; set; }

    /// <summary>#AUTH-59: kid của PreviousSecretKey. Default "v0".</summary>
    public string PreviousSigningKeyId { get; set; } = "v0";
}
