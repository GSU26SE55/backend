using AuthService.Application.Configuration;
using SharedContracts.Common.Responses;

namespace AuthService.Application.Validation;

/// <summary>
/// #AUTH-53: validation policy được config-driven thông qua <see cref="PasswordPolicyOptions"/>.
/// Bootstrap qua <see cref="Configure"/> ở DI startup; nếu chưa configure, dùng default values.
/// </summary>
public static class PasswordPolicy
{
    private static PasswordPolicyOptions _options = new();

    /// <summary>Đăng ký options lúc app startup (gọi từ DI bootstrap).</summary>
    public static void Configure(PasswordPolicyOptions options) => _options = options ?? new PasswordPolicyOptions();

    public static int MinLength => _options.MinLength;
    public static int MaxLength => _options.MaxLength;

    public static void AddStrongPasswordErrors(ICollection<Errors> errors, string? password, string field, string label)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            errors.Add(new Errors { Field = field, Detail = $"{label} không được để trống." });
            return;
        }

        if (password.Length < _options.MinLength)
        {
            errors.Add(new Errors { Field = field, Detail = $"{label} tối thiểu {_options.MinLength} ký tự." });
            return;
        }

        if (password.Length > _options.MaxLength)
        {
            errors.Add(new Errors { Field = field, Detail = $"{label} tối đa {_options.MaxLength} ký tự." });
            return;
        }

        var missing = CollectMissingRequirements(password);
        if (missing.Count > 0)
        {
            errors.Add(new Errors
            {
                Field = field,
                Detail = $"{label} phải có {string.Join(", ", missing)}."
            });
        }
    }

    private static List<string> CollectMissingRequirements(string password)
    {
        var missing = new List<string>(4);
        if (_options.RequireUppercase && !password.Any(char.IsUpper))
            missing.Add("chữ hoa");
        if (_options.RequireLowercase && !password.Any(char.IsLower))
            missing.Add("chữ thường");
        if (_options.RequireDigit && !password.Any(char.IsDigit))
            missing.Add("chữ số");
        if (_options.RequireSpecialChar && !password.Any(IsSpecialChar))
            missing.Add("ký tự đặc biệt");
        return missing;
    }

    private static bool IsSpecialChar(char c)
        => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c);
}
