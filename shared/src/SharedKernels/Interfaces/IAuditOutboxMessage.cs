namespace SharedKernels.Interfaces;

/// <summary>
/// GH-728 — hợp đồng chung cho bảng <c>*_audit_outbox</c> của 6 service có audit
/// (Auth, Battery, Ticket, Notification, Sms, FileStorage).
///
/// <para>Sáu entity này vốn đã giống hệt nhau về cấu trúc nhưng không có kiểu chung, nên
/// không viết được một khung replay dùng chung. Interface này chỉ khai báo lại các thuộc
/// tính ĐÃ TỒN TẠI — không thêm cột, không cần migration.</para>
///
/// <para><b>Vì sao replay đọc outbox chứ không đọc <c>{service}_audit_logs</c>:</b> sáu bảng
/// audit-log KHÔNG đồng nhất (<c>AuthService.AuditLog</c> dùng <c>IpAddress</c>/<c>UserAgent</c>
/// và có cột riêng; <c>SmsService.SmsAuditLog</c> thậm chí không phải bảng audit-event).
/// Trong khi đó <see cref="Payload"/> của outbox chính là <c>AuditCreatedEventV1</c> đã
/// serialize — đúng thứ cần phát lại, và giống nhau ở cả 6 service.</para>
/// </summary>
public interface IAuditOutboxMessage
{
    /// <summary>Idempotency key của audit event (Guid v7). Giữ nguyên khi replay.</summary>
    Guid EventId { get; }

    /// <summary>Tên type của event trong <see cref="Payload"/> — hiện luôn là <c>AuditCreatedEventV1</c>.</summary>
    string EventType { get; }

    /// <summary>JSON của <c>AuditCreatedEventV1</c>.</summary>
    string Payload { get; }
}
