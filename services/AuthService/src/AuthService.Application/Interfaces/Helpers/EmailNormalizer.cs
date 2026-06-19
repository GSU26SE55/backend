namespace AuthService.Application.Interfaces.Helpers;

/// <summary>
/// #AUTH-39: Chuẩn hóa email một chỗ duy nhất để tránh inconsistency giữa register / login / change-email /
/// forgot-password và các nơi query DB.
///
/// Quy ước:
/// - Trim whitespace.
/// - Lowercase invariant culture (PostgreSQL mặc định case-sensitive, lowercase trước query để khớp index).
/// - KHÔNG validate format ở đây — validation tách riêng ở command validator.
/// </summary>
public static class EmailNormalizer
{
    public static string Normalize(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return string.Empty;
        return email.Trim().ToLowerInvariant();
    }
}
