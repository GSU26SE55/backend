using System.Text.RegularExpressions;
using SharedContracts.Common.Responses;

namespace AuthService.Application.Validation;

public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 100;

    private static readonly Regex StrongPasswordRegex = new(
        @"^(?=.*[A-Z])(?=.*[a-z])(?=.*\d)(?=.*[!@#$%^&*()_+\-=\[\]{};':""\\|,.<>\/?]).{8,}$",
        RegexOptions.Compiled);

    public static void AddStrongPasswordErrors(ICollection<Errors> errors, string? password, string field, string label)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            errors.Add(new Errors { Field = field, Detail = $"{label} không được để trống." });
            return;
        }

        if (password.Length > MaxLength)
        {
            errors.Add(new Errors { Field = field, Detail = $"{label} tối đa {MaxLength} ký tự." });
            return;
        }

        if (!StrongPasswordRegex.IsMatch(password))
        {
            errors.Add(new Errors
            {
                Field = field,
                Detail = $"{label} phải tối thiểu {MinLength} ký tự, có chữ hoa, chữ thường, số và ký tự đặc biệt."
            });
        }
    }
}
