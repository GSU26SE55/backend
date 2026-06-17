using SharedKernels.Domain;

namespace SmsService.Domain.Entities;

/// <summary>
/// Device gateway (Flutter app trên Android có SIM thật) được admin cấp <c>ApiKeyHash</c> (BCrypt).
/// Mỗi device có <c>DailyLimit</c> riêng, reset vào ngày UTC mới.
/// `xmin` (Postgres) optimistic concurrency để tránh over-claim khi 2 request đồng thời tăng <c>SentToday</c>.
/// </summary>
public class SmsGatewayDevice : AuditableEntity
{
    public string DeviceName { get; set; } = default!;

    public string DeviceCode { get; set; } = default!;

    /// <summary>BCrypt hash của API key plaintext. Plaintext chỉ trả về client 1 lần khi tạo device.</summary>
    public string ApiKeyHash { get; set; } = default!;

    public bool IsActive { get; set; } = true;

    public DateTime? RevokedAt { get; set; }

    public int DailyLimit { get; set; } = 100;

    public int SentToday { get; set; }

    public DateOnly? SentTodayDate { get; set; }

    public DateTime? LastSeenAt { get; set; }

    public string? LastSeenIp { get; set; }

    // ── Domain methods ────────────────────────────────────────────────

    public void Touch(string? ip, DateTime now)
    {
        LastSeenAt = now;
        LastSeenIp = ip;
        UpdatedAt = now;
    }

    public void ResetDailyCounterIfNeeded(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        if (SentTodayDate != today)
        {
            SentTodayDate = today;
            SentToday = 0;
        }
    }

    public void IncrementSent(DateTime now)
    {
        ResetDailyCounterIfNeeded(now);
        SentToday++;
        UpdatedAt = now;
    }

    public void Revoke(DateTime now)
    {
        IsActive = false;
        RevokedAt = now;
        UpdatedAt = now;
    }
}
