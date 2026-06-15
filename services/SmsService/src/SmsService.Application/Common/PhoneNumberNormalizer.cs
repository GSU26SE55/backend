using System.Text.RegularExpressions;

namespace SmsService.Application.Common;

/// <summary>
/// Normalize số điện thoại VN về dạng E.164 (<c>+84xxxxxxxxx</c>).
/// Trả <c>null</c> nếu input không hợp lệ — handler dùng làm validation.
/// </summary>
public static class PhoneNumberNormalizer
{
    private static readonly Regex Cleanup = new(@"[\s\-\(\)\.]", RegexOptions.Compiled);
    private static readonly Regex E164 = new(@"^\+?[0-9]{9,15}$", RegexOptions.Compiled);

    public static string? NormalizeVn(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = Cleanup.Replace(raw.Trim(), string.Empty);

        if (s.StartsWith("0") && s.Length == 10)
            s = "+84" + s[1..];
        else if (s.StartsWith("84") && s.Length == 11)
            s = "+" + s;

        return E164.IsMatch(s) ? s : null;
    }
}
