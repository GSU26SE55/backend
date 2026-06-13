using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;

namespace BatteryService.Application.Interfaces;

/// <summary>
/// Sprint IoT-1 (#243) — phát/rotate/verify API key per-device cho ESP32 ingest.
/// </summary>
public interface IIotApiKeyService
{
    /// <summary>
    /// Sinh raw key 256-bit (43 ký tự base64url) + SHA-256 hash + last-four.
    /// Plaintext chỉ trả 1 lần cho admin lưu — DB chỉ giữ hash.
    /// </summary>
    GeneratedApiKey GenerateKey();

    /// <summary>SHA-256 raw → hex string (64 chars). Dùng để lookup/verify constant-time.</summary>
    string Hash(string rawKey);

    /// <summary>Compare 2 hash constant-time.</summary>
    bool VerifyHash(string providedHashOrRaw, string storedHash);

    /// <summary>Device tìm theo hash + scope. Trả null nếu không match hoặc revoked/disabled.</summary>
    Task<IotDevice?> FindDeviceByRawKeyAsync(string rawKey, IotApiKeyScopeEnum requiredScope, CancellationToken ct);
}

public record GeneratedApiKey(string RawKey, string Hash, string LastFour);
