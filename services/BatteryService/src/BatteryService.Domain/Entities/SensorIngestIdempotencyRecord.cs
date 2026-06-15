using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

/// <summary>
/// Sprint IoT-2 #IoT2-16 (S3-BE-03) — idempotency persistence cho POST /api/sensor-readings/batch.
/// Lookup key = <c>(DeviceCode, IdempotencyKey)</c>; trùng → trả response cũ (200), không insert reading.
/// TTL 24h — <c>IotIdempotencyRetentionBackgroundService</c> dọn dẹp.
/// </summary>
public class SensorIngestIdempotencyRecord : AuditableEntity
{
    public string DeviceCode { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Số reading đã insert ở request đầu (để trả lại y nguyên cho retry).</summary>
    public int Inserted { get; set; }

    public int Skipped { get; set; }

    public int TotalReceived { get; set; }

    public string? Message { get; set; }

    /// <summary>Hết hạn — record sau ngày này được background xoá.</summary>
    public DateTime ExpiresAt { get; set; }
}
