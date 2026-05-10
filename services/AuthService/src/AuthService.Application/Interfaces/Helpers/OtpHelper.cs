using System.Security.Cryptography;

namespace AuthService.Application.Interfaces.Helpers;

public static class OtpHelper
{
    /// <summary>
    /// Sinh OTP gồm các chữ số 0-9 bằng RandomNumberGenerator (cryptographic, không phải Random).
    /// </summary>
    public static string GenerateOtp(int length = 6)
    {
        if (length <= 0)
            length = 6;
        var buffer = new byte[length];
        RandomNumberGenerator.Fill(buffer);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = (char)('0' + (buffer[i] % 10));
        return new string(chars);
    }
}
