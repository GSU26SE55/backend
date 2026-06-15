using SharedKernels.Domain;
using SmsService.Domain.Enums;

namespace SmsService.Domain.Entities;

/// <summary>
/// Append-only audit log cho mọi sự kiện trong vòng đời SMS.
/// Kế thừa <see cref="BaseEntity"/> thay vì <see cref="AuditableEntity"/> vì:
/// (1) Audit log không có khái niệm "update" hay "soft delete";
/// (2) <c>CreatedAt</c> được handler set thủ công, không cần interceptor override.
/// </summary>
public class SmsAuditLog : BaseEntity
{
    public Guid SmsMessageId { get; set; }

    public SmsAuditEvent Event { get; set; }

    public string? DeviceCode { get; set; }

    public string? Detail { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
