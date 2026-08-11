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
            errors.Add(new Errors { Field = field, Detail = $"{label} must not be empty." });
            return;
        }

        if (password.Length < _options.MinLength)
        {
            errors.Add(new Errors { Field = field, Detail = $"{label} must be at least {_options.MinLength} characters." });
            return;
        }

        if (password.Length > _options.MaxLength)
        {
            errors.Add(new Errors { Field = field, Detail = $"{label} must not exceed {_options.MaxLength} characters." });
            return;
        }

        var missing = CollectMissingRequirements(password);
        if (missing.Count > 0)
        {
            errors.Add(new Errors
            {
                Field = field,
                Detail = $"{label} must contain {string.Join(", ", missing)}."
            });
        }
    }

    private static List<string> CollectMissingRequirements(string password)
    {
        var missing = new List<string>(4);
        if (_options.RequireUppercase && !password.Any(char.IsUpper))
            missing.Add("at least one uppercase letter");
        if (_options.RequireLowercase && !password.Any(char.IsLower))
            missing.Add("at least one lowercase letter");
        if (_options.RequireDigit && !password.Any(char.IsDigit))
            missing.Add("at least one digit");
        if (_options.RequireSpecialChar && !password.Any(IsSpecialChar))
            missing.Add("at least one special character");
        return missing;
    }

    private static bool IsSpecialChar(char c)
        => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c);
}
