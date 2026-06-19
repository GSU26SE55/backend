using System.Security.Cryptography;
using System.Text;

namespace AuthService.Application.Interfaces.Helpers;

/// <summary>
/// #AUTH-01: Hash refresh token bằng SHA-256 trước khi lưu DB.
///
/// Plaintext refresh token chỉ tồn tại trong response trả về client. DB chỉ lưu hash.
/// Khi client gửi lại token, hash để lookup. Tránh tình huống DB leak → attacker dùng được token ngay.
///
/// SHA-256 (không có salt) là đủ vì:
/// - Refresh token là 32-byte random (Guid.NewGuid().ToString("N")) → 128 bit entropy, unguessable.
/// - Không cần salt vì không có user input có thể đoán (khác password).
/// - Constant-time so sánh khi lookup bằng EF (sequential scan trên hash index, vẫn fast).
/// </summary>
public static class RefreshTokenHasher
{
    /// <summary>Hash plaintext refresh token bằng SHA-256, trả hex lowercase 64 chars.</summary>
    public static string Hash(string plainToken)
    {
        if (string.IsNullOrEmpty(plainToken))
            throw new ArgumentException("Token cannot be empty.", nameof(plainToken));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
