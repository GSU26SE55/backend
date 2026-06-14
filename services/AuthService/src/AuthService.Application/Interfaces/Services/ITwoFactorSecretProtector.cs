namespace AuthService.Application.Interfaces.Services;

/// <summary>
/// Wrap ASP.NET Data Protection để encrypt/decrypt <c>Account.TwoFactorSecret</c>.
/// Format wrap output: <c>enc:v1:{base64(ciphertext)}</c> — prefix giúp detect plaintext legacy + key rotation tương lai.
/// </summary>
public interface ITwoFactorSecretProtector
{
    /// <summary>Encrypt plaintext secret → trả về chuỗi có prefix <c>enc:v1:</c>.</summary>
    string Protect(string plaintext);

    /// <summary>
    /// Decrypt. Nếu input không có prefix <c>enc:</c> → coi như legacy plaintext, trả về nguyên giá trị.
    /// Lỗi giải mã → throw <see cref="System.Security.Cryptography.CryptographicException"/>.
    /// </summary>
    string Unprotect(string maybeProtected);

    /// <summary>True nếu chuỗi đã được protect (có prefix <c>enc:</c>).</summary>
    bool IsProtected(string value);
}
