using AuthService.Application.Interfaces.Services;
using OtpNet;

namespace AuthService.Infrastructure.Implements.Services;

/// <summary>
/// TOTP RFC 6238 implementation dùng thư viện <c>Otp.NET</c>.
/// SHA1 / 6 digits / period 30s — tương thích Google + Microsoft Authenticator.
/// </summary>
public class TotpService : ITotpService
{
    private const int SecretByteLength = 20; // 20 bytes = 160 bits — chuẩn Google Authenticator
    private const int Digits = 6;
    private const int PeriodSeconds = 30;

    public string GenerateSecret()
    {
        var bytes = KeyGeneration.GenerateRandomKey(SecretByteLength);
        return Base32Encoding.ToString(bytes);
    }

    public string BuildOtpAuthUri(string secret, string accountLabel, string issuer)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("secret required", nameof(secret));
        if (string.IsNullOrWhiteSpace(accountLabel))
            throw new ArgumentException("accountLabel required", nameof(accountLabel));
        if (string.IsNullOrWhiteSpace(issuer))
            throw new ArgumentException("issuer required", nameof(issuer));

        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedLabel = Uri.EscapeDataString(accountLabel);
        return $"otpauth://totp/{encodedIssuer}:{encodedLabel}?secret={secret}&issuer={encodedIssuer}&algorithm=SHA1&digits={Digits}&period={PeriodSeconds}";
    }

    public bool VerifyCode(string secret, string code, int allowedDriftSteps = 1)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
            return false;
        if (code.Length != Digits || !code.All(char.IsDigit))
            return false;

        byte[] secretBytes;
        try
        { secretBytes = Base32Encoding.ToBytes(secret); }
        catch { return false; }

        var totp = new Totp(secretBytes, step: PeriodSeconds, mode: OtpHashMode.Sha1, totpSize: Digits);
        var window = new VerificationWindow(previous: allowedDriftSteps, future: allowedDriftSteps);
        return totp.VerifyTotp(code, out _, window);
    }
}
