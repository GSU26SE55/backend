namespace AuthService.Application.Interfaces.Services;

/// <summary>
/// #AUTH-58: Redis-backed store cho 2FA SMS OTP. Key gắn với challengeToken (1-1 với phiên login).
/// </summary>
public interface ITwoFactorSmsOtpStore
{
    /// <summary>Lưu OTP. TTL ngắn (3 phút) — ngắn hơn challenge TTL để buộc user nhập sớm.</summary>
    Task SetAsync(string challengeToken, string otp, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Đọc OTP hiện tại. null nếu chưa request hoặc đã expire.</summary>
    Task<string?> GetAsync(string challengeToken, CancellationToken ct = default);

    /// <summary>Xoá OTP sau verify thành công.</summary>
    Task InvalidateAsync(string challengeToken, CancellationToken ct = default);
}
