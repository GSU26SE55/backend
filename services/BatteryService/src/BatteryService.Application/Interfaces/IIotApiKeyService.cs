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
    /// </summary>
    /// <remarks>
    /// GH-724 — hàm này CHỈ sinh giá trị; nó không quyết định cái gì được lưu.
    /// Từ commit <c>82b56569</c> (2026-07-16, "display iotkey") hệ thống lưu <b>cả</b>
    /// <see cref="IotDevice.ApiKeyHash"/> (để verify constant-time) <b>lẫn</b>
    /// <see cref="IotDevice.ApiKeyPlaintext"/> (để Admin xem lại key trên
    /// <c>GET /api/admin/iot-devices/{id}</c> mà không phải rotate).
    ///
    /// Đây là đánh đổi CÓ CHỦ Ý, không phải sơ suất: ESP32 ngoài hiện trường cần flash lại
    /// key nên Admin phải đọc lại được. Hệ quả bảo mật đã chấp nhận: ai đọc được DB hoặc
    /// gọi được endpoint admin GetById thì lấy được credential thiết bị.
    ///
    /// Doc cũ ở đây ghi "DB chỉ giữ hash" — SAI so với hiện thực, đã sửa theo #724 (quyết
    /// định: giữ nguyên hành vi, sửa tài liệu).
    ///
    /// Lưu ý: MQTT password thì KHÁC — chỉ có <see cref="IotDevice.MqttPasswordHash"/>,
    /// không lưu plaintext. Xem <see cref="GenerateMqttCredential"/>.
    /// </remarks>
    GeneratedApiKey GenerateKey();

    /// <summary>SHA-256 raw → hex string (64 chars). Dùng để lookup/verify constant-time.</summary>
    string Hash(string rawKey);

    /// <summary>Compare 2 hash constant-time.</summary>
    bool VerifyHash(string providedHashOrRaw, string storedHash);

    /// <summary>Device tìm theo hash + scope. Trả null nếu không match hoặc revoked/disabled.</summary>
    Task<IotDevice?> FindDeviceByRawKeyAsync(string rawKey, IotApiKeyScopeEnum requiredScope, CancellationToken ct);

    /// <summary>
    /// GH-785 — tra thiết bị theo khoá, PHÂN BIỆT "khoá không hợp lệ" với "khoá đúng nhưng thiếu scope".
    /// </summary>
    /// <remarks>
    /// Bản cũ gộp cả hai thành <c>null</c> nên tầng xác thực trả 401 cho cả hai. Sai hợp đồng:
    /// 401 nghĩa là "chưa xác thực được anh là ai" — thiết bị sẽ đi xoay khoá, cấp lại khoá, mà
    /// không bao giờ nhận ra vấn đề thật là THIẾU QUYỀN. 403 nói đúng chuyện đang xảy ra.
    /// </remarks>
    Task<DeviceKeyLookup> LookupDeviceByRawKeyAsync(string rawKey, IotApiKeyScopeEnum requiredScope, CancellationToken ct);

    /// <summary>
    /// Sprint IoT-2 #IoT2-26 — sinh MQTT username (= deviceCode lowercase) + raw password (~24 chars base64url).
    /// Trả plaintext + PBKDF2 hash. Plaintext chỉ trả 1 lần khi tạo/rotate device.
    /// </summary>
    GeneratedMqttCredential GenerateMqttCredential(string deviceCode);
}

public record GeneratedApiKey(string RawKey, string Hash, string LastFour);

public record GeneratedMqttCredential(string Username, string RawPassword, string PasswordHash);

/// <summary>GH-785 — kết quả tra khoá thiết bị.</summary>
/// <param name="Device">Thiết bị, hoặc null khi khoá không dùng được.</param>
/// <param name="ScopeDenied">
/// True khi khoá HỢP LỆ nhưng thiếu scope — người gọi nên trả 403, không phải 401.
/// </param>
public readonly record struct DeviceKeyLookup(IotDevice? Device, bool ScopeDenied)
{
    public static DeviceKeyLookup NotFound => new(null, false);
    public static DeviceKeyLookup Denied => new(null, true);
    public static DeviceKeyLookup Ok(IotDevice device) => new(device, false);
}
