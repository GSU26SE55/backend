namespace AuthService.Application.Interfaces.Services;

/// <summary>
/// Lưu challenge token giữa <c>POST /api/auth/login</c> (bước 1) và <c>POST /api/auth/login/verify-2fa</c> (bước 2).
/// Backed by Redis với TTL 5 phút.
/// </summary>
public interface ITwoFactorChallengeStore
{
    /// <summary>Tạo challenge mới, trả token (GUID string) đã lưu vào Redis với TTL.</summary>
    Task<string> CreateAsync(Guid accountId, string ipAddress, string userAgent, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Lấy challenge data; null nếu không tồn tại hoặc đã expire/invalidate.</summary>
    Task<TwoFactorChallengeData?> GetAsync(string token, CancellationToken ct = default);

    /// <summary>Tăng attempts atomic. Trả về số attempts sau khi increment.</summary>
    Task<int> IncrementAttemptsAsync(string token, CancellationToken ct = default);

    /// <summary>Xóa challenge (dùng khi verify thành công hoặc vượt max attempts).</summary>
    Task InvalidateAsync(string token, CancellationToken ct = default);
}

public sealed record TwoFactorChallengeData(Guid AccountId, string IpAddress, string UserAgent, int Attempts, DateTime CreatedAtUtc);
