namespace AuthService.Application.Configuration;

/// <summary>
/// #AUTH-53: cấu hình password policy động. Bind từ section <c>PasswordPolicy</c>.
/// Default match hardcoded value cũ (backward compatible) — deployer override khi cần.
/// </summary>
public class PasswordPolicyOptions
{
    public const string SectionName = "PasswordPolicy";

    /// <summary>Độ dài tối thiểu. Default 8.</summary>
    public int MinLength { get; set; } = 8;

    /// <summary>Độ dài tối đa. Default 100.</summary>
    public int MaxLength { get; set; } = 100;

    /// <summary>Bắt buộc có ít nhất 1 chữ hoa. Default true.</summary>
    public bool RequireUppercase { get; set; } = true;

    /// <summary>Bắt buộc có ít nhất 1 chữ thường. Default true.</summary>
    public bool RequireLowercase { get; set; } = true;

    /// <summary>Bắt buộc có ít nhất 1 chữ số. Default true.</summary>
    public bool RequireDigit { get; set; } = true;

    /// <summary>Bắt buộc có ít nhất 1 ký tự đặc biệt. Default true.</summary>
    public bool RequireSpecialChar { get; set; } = true;
}
