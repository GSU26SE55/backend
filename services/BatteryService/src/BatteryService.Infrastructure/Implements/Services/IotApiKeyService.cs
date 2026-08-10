using System.Security.Cryptography;
using System.Text;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BatteryService.Infrastructure.Implements.Services;

/// <summary>
/// Sprint IoT-1 (#243) — implement <see cref="IIotApiKeyService"/>.
///
/// Format key: "iotk_{base64url-32bytes}" (~ 47 chars). Prefix giúp nhận diện dễ trong log.
/// </summary>
public class IotApiKeyService : IIotApiKeyService
{
    private const string KeyPrefix = "iotk_";
    private readonly IBatteryUnitOfWork _unitOfWork;

    public IotApiKeyService(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public GeneratedApiKey GenerateKey()
    {
        Span<byte> buffer = stackalloc byte[32];
        RandomNumberGenerator.Fill(buffer);
        var body = Base64UrlEncode(buffer);
        var raw = KeyPrefix + body;
        var hash = Hash(raw);
        var lastFour = raw[^4..];
        return new GeneratedApiKey(raw, hash, lastFour);
    }

    public string Hash(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
            return string.Empty;

        Span<byte> hash = stackalloc byte[32];
        var bytes = Encoding.UTF8.GetBytes(rawKey);
        SHA256.HashData(bytes, hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public bool VerifyHash(string providedHashOrRaw, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(providedHashOrRaw) || string.IsNullOrWhiteSpace(storedHash))
            return false;

        var providedHash = providedHashOrRaw.StartsWith(KeyPrefix, StringComparison.Ordinal)
            ? Hash(providedHashOrRaw)
            : providedHashOrRaw;

        var a = Encoding.UTF8.GetBytes(providedHash);
        var b = Encoding.UTF8.GetBytes(storedHash);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    public async Task<IotDevice?> FindDeviceByRawKeyAsync(string rawKey, IotApiKeyScopeEnum requiredScope, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawKey) || !rawKey.StartsWith(KeyPrefix, StringComparison.Ordinal))
            return null;

        var hash = Hash(rawKey);

        var device = await _unitOfWork.IotDevices
            .GetAllAsync()
            .Where(d => !d.IsDeleted
                        && d.ApiKeyHash == hash
                        && d.ApiKeyRevokedAt == null
                        && d.Status != IotDeviceStatusEnum.Disabled
                        && d.Status != IotDeviceStatusEnum.Decommissioned)
            .FirstOrDefaultAsync(ct);

        if (device is null)
            return null;
        if ((device.ApiKeyScopes & requiredScope) != requiredScope)
            return null;

        return device;
    }

    /// <inheritdoc />
    public async Task<DeviceKeyLookup> LookupDeviceByRawKeyAsync(
        string rawKey, IotApiKeyScopeEnum requiredScope, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawKey) || !rawKey.StartsWith(KeyPrefix, StringComparison.Ordinal))
            return DeviceKeyLookup.NotFound;

        var hash = Hash(rawKey);

        var device = await _unitOfWork.IotDevices
            .GetAllAsync()
            .Where(d => !d.IsDeleted
                        && d.ApiKeyHash == hash
                        && d.ApiKeyRevokedAt == null
                        && d.Status != IotDeviceStatusEnum.Disabled
                        && d.Status != IotDeviceStatusEnum.Decommissioned)
            .FirstOrDefaultAsync(ct);

        if (device is null)
            return DeviceKeyLookup.NotFound;

        // GH-785 — khoá ĐÚNG nhưng thiếu quyền: đây là 403, không phải 401. Gộp làm một khiến
        // người vận hành đi xoay khoá mãi mà không bao giờ thấy vấn đề thật là thiếu scope.
        if ((device.ApiKeyScopes & requiredScope) != requiredScope)
            return DeviceKeyLookup.Denied;

        return DeviceKeyLookup.Ok(device);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    // Sprint IoT-2 #IoT2-26 — MQTT credential per-device.
    // GH-784 — PHẢI khớp định dạng `$7$` của Mosquitto, không phải một biến thể PBKDF2 tự đặt.
    // Bản cũ sinh "PBKDF2$sha256${iter}${salt}${hash}" với SHA256/32 byte và tự nhận là
    // "Mosquitto-compatible". Mosquitto KHÔNG hiểu tiền tố đó: nó chỉ đọc `$7$<iter>$<salt>$<hash>`
    // với PBKDF2-HMAC-SHA512, output 64 byte. Hậu quả: mọi credential thiết bị bị từ chối kể cả khi
    // đã nằm đúng trong file passwd — sai từ gốc chứ không phải sai ở khâu đồng bộ.
    // Đối chiếu bản ghi thật do `mosquitto_passwd` sinh:
    //   backend-bridge:$7$101$<12-byte salt b64>$<64-byte hash b64>
    private const int MqttSaltBytes = 12;
    private const int MqttHashBytes = 64;

    /// <summary>
    /// Số vòng lặp ghi thẳng vào bản ghi; Mosquitto đọc lại từ đó nên không bắt buộc bằng mặc định
    /// 101 của <c>mosquitto_passwd</c>. Giữ cao hơn hẳn vì 101 vòng là quá yếu cho một mật khẩu
    /// dài hạn của thiết bị.
    /// </summary>
    private const int MqttPbkdf2Iterations = 10_000;

    /// <summary>Tiền tố định danh thuật toán của Mosquitto — `$7$` = PBKDF2-HMAC-SHA512.</summary>
    private const string MosquittoPbkdf2Sha512Prefix = "$7$";

    public GeneratedMqttCredential GenerateMqttCredential(string deviceCode)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
            throw new ArgumentException("deviceCode required", nameof(deviceCode));

        // Username = deviceCode lowercase (Mosquitto/EMQX best practice — không chứa ký tự nhạy cảm).
        var username = deviceCode.Trim().ToLowerInvariant();

        // Raw password: 18 byte random → base64url ~24 chars.
        Span<byte> raw = stackalloc byte[18];
        RandomNumberGenerator.Fill(raw);
        var rawPassword = Base64UrlEncode(raw);

        // PBKDF2-HMAC-SHA512 + salt, xuất ra đúng định dạng `$7$` mà Mosquitto đọc được (GH-784).
        Span<byte> salt = stackalloc byte[MqttSaltBytes];
        RandomNumberGenerator.Fill(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(rawPassword),
            salt.ToArray(),
            MqttPbkdf2Iterations,
            HashAlgorithmName.SHA512,   // GH-784 — Mosquitto `$7$` là SHA512, KHÔNG phải SHA256
            MqttHashBytes);

        // Định dạng CHÍNH XÁC của Mosquitto: $7$<iterations>$<salt b64>$<hash b64>.
        var stored = $"{MosquittoPbkdf2Sha512Prefix}{MqttPbkdf2Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        return new GeneratedMqttCredential(username, rawPassword, stored);
    }
}
