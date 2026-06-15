using SharedKernels.Domain;
using SmsService.Domain.Enums;

namespace SmsService.Domain.Entities;

/// <summary>
/// 1 SMS được queue trong gateway. State machine: <see cref="SmsStatus"/>.
/// `Message` nullable vì <c>SmsMessageRedactorBackgroundService</c> có thể xóa cột này sau 24h khi <c>Sent</c>.
/// `xmin` (Postgres) làm optimistic concurrency token tránh 2 device cùng claim 1 row — config ở <c>SmsDbContext.OnModelCreating</c>.
/// </summary>
public class SmsMessage : AuditableEntity
{
    public string PhoneNumber { get; set; } = default!;

    /// <summary>Nội dung SMS đầy đủ. Có thể <c>null</c> sau khi redactor xóa.</summary>
    public string? Message { get; set; }

    public SmsStatus Status { get; set; } = SmsStatus.Pending;

    public int RetryCount { get; set; }

    public int MaxRetryCount { get; set; } = 3;

    public string? ErrorMessage { get; set; }

    public string? Category { get; set; }

    public string SourceService { get; set; } = default!;

    public Guid CorrelationId { get; set; }

    /// <summary>Nếu set: chỉ device khớp <c>DeviceCode</c> mới claim được. Null = broadcast cho mọi device.</summary>
    public string? TargetDeviceCode { get; set; }

    public string? GatewayDeviceCode { get; set; }

    public Guid? GatewayDeviceId { get; set; }

    public DateTime? PickedAt { get; set; }

    public DateTime? SentAt { get; set; }

    public DateTime? FailedAt { get; set; }

    public DateTime? RedactedAt { get; set; }

    // ── Domain methods ────────────────────────────────────────────────

    /// <summary>Device claim message (chuyển Pending → Sending).</summary>
    public void Claim(string deviceCode, Guid deviceId, DateTime now)
    {
        Status = SmsStatus.Sending;
        PickedAt = now;
        GatewayDeviceCode = deviceCode;
        GatewayDeviceId = deviceId;
        UpdatedAt = now;
    }

    /// <summary>Device báo gửi thành công. Side-effect <c>SmsDeliveryReportEvent</c> đi qua Outbox (xem handler).</summary>
    public void MarkSent(DateTime now)
    {
        Status = SmsStatus.Sent;
        SentAt = now;
        ErrorMessage = null;
        UpdatedAt = now;
    }

    /// <summary>Failed lần này nhưng còn retry → quay về Pending. Bump RetryCount.</summary>
    public void MarkRetry(string? error, DateTime now)
    {
        RetryCount++;
        ErrorMessage = error;
        Status = SmsStatus.Pending;
        GatewayDeviceCode = null;
        GatewayDeviceId = null;
        PickedAt = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Failed lần cuối (RetryCount + 1 == MaxRetryCount). Bump RetryCount để cuối luồng
    /// <c>RetryCount == MaxRetryCount</c> nhất quán semantic. Side-effect <c>SmsFailedEvent</c>
    /// đi qua Outbox (xem handler).
    /// </summary>
    public void MarkFailedFinal(string? error, DateTime now)
    {
        RetryCount++;
        Status = SmsStatus.Failed;
        FailedAt = now;
        ErrorMessage = error;
        UpdatedAt = now;
    }

    public void Cancel(DateTime now)
    {
        Status = SmsStatus.Cancelled;
        UpdatedAt = now;
    }

    /// <summary>
    /// <c>StaleSmsReaperBackgroundService</c> gọi để revert <c>Sending</c> → <c>Pending</c> khi
    /// vượt 5 phút mà device chưa report. KHÔNG bump RetryCount (không tính là attempt thất bại từ phía device).
    /// </summary>
    public void ReapStaleClaim(DateTime now)
    {
        Status = SmsStatus.Pending;
        GatewayDeviceCode = null;
        GatewayDeviceId = null;
        PickedAt = null;
        UpdatedAt = now;
    }

    public void Redact(DateTime now)
    {
        Message = null;
        RedactedAt = now;
        UpdatedAt = now;
    }
}
