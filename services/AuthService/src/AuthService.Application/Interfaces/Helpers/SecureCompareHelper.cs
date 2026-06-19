using System.Security.Cryptography;
using System.Text;

namespace AuthService.Application.Interfaces.Helpers;

/// <summary>
/// Constant-time string comparison cho các secret nhạy cảm (OTP, reset code, 2FA backup code).
/// Tránh timing side-channel attack so với <c>string.Equals(StringComparison.Ordinal)</c>.
/// </summary>
public static class SecureCompareHelper
{
    /// <summary>
    /// So sánh 2 chuỗi theo thời gian cố định. Trả false nếu một trong hai null/empty hoặc khác độ dài.
    /// Encoding UTF-8.
    /// </summary>
    public static bool FixedTimeEquals(string? expected, string? actual)
    {
        if (expected is null || actual is null)
            return false;

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);

        if (expectedBytes.Length != actualBytes.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
