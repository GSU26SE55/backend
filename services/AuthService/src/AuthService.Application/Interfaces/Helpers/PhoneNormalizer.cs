using System.Text.RegularExpressions;

namespace AuthService.Application.Interfaces.Helpers;

/// <summary>
/// #AUTH-39: Chuẩn hóa số điện thoại VN về format E.164 (<c>+84xxxxxxxxx</c>).
///
/// Quy ước:
/// - Strip whitespace, dấu '-', dấu '.', '(', ')'.
/// - Nếu bắt đầu '0' → thay bằng '+84' (giả định VN).
/// - Nếu bắt đầu '84' (không '+') → thêm '+'.
/// - Nếu đã '+' → giữ nguyên.
/// - Empty / null → trả empty string.
/// - KHÔNG validate độ dài / prefix — validation tách riêng.
/// </summary>
public static class PhoneNormalizer
{
    private static readonly Regex CleanupRegex = new(@"[\s\-\.\(\)]", RegexOptions.Compiled);

    public static string Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return string.Empty;

        var cleaned = CleanupRegex.Replace(phone.Trim(), string.Empty);
        if (cleaned.Length == 0)
            return string.Empty;

        if (cleaned.StartsWith('+'))
            return cleaned;

        if (cleaned.StartsWith("84", StringComparison.Ordinal))
            return "+" + cleaned;

        if (cleaned.StartsWith('0'))
            return "+84" + cleaned[1..];

        // Số nước ngoài không '0' / '+': giữ nguyên (caller validate).
        return cleaned;
    }
}
