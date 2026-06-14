using System.Security.Cryptography;
using AuthService.Application.Interfaces.Services;
using Microsoft.AspNetCore.DataProtection;

namespace AuthService.Infrastructure.Implements.Services;

/// <summary>
/// Wrap <see cref="IDataProtector"/> để encrypt/decrypt TOTP secret.
/// Format: <c>enc:v1:{base64(ciphertext)}</c>. Prefix giúp detect legacy plaintext + cho phép key rotation (<c>enc:v2:</c> tương lai).
/// </summary>
public class TwoFactorSecretProtector : ITwoFactorSecretProtector
{
    private const string Purpose = "AuthService.Account.TwoFactorSecret.v1";
    private const string PrefixV1 = "enc:v1:";

    private readonly IDataProtector _protector;

    public TwoFactorSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            throw new ArgumentException("plaintext required", nameof(plaintext));
        var ciphertext = _protector.Protect(System.Text.Encoding.UTF8.GetBytes(plaintext));
        return PrefixV1 + Convert.ToBase64String(ciphertext);
    }

    public string Unprotect(string maybeProtected)
    {
        if (string.IsNullOrEmpty(maybeProtected))
            throw new ArgumentException("value required", nameof(maybeProtected));
        if (!maybeProtected.StartsWith(PrefixV1, StringComparison.Ordinal))
        {
            // Legacy plaintext — trả về nguyên giá trị, caller chịu trách nhiệm re-encrypt nếu cần.
            return maybeProtected;
        }
        var base64 = maybeProtected.Substring(PrefixV1.Length);
        byte[] cipher;
        try
        { cipher = Convert.FromBase64String(base64); }
        catch (FormatException ex) { throw new CryptographicException("Invalid base64 in protected secret.", ex); }
        var plainBytes = _protector.Unprotect(cipher);
        return System.Text.Encoding.UTF8.GetString(plainBytes);
    }

    public bool IsProtected(string value)
        => !string.IsNullOrEmpty(value) && value.StartsWith(PrefixV1, StringComparison.Ordinal);
}
