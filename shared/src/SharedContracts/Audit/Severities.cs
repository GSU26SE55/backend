namespace SharedContracts.Audit;

/// <summary>
/// 4 mức severity cố định cho audit event (Sprint audit <c>#AUDIT-02</c>, Phụ lục B §B.2.1).
/// Dùng làm giá trị string cho <c>AuditCreatedEventV1.Severity</c> + cột <c>severity</c>.
///
/// <para>Retention: <see cref="Critical"/> và <see cref="Security"/> giữ <b>vĩnh viễn</b> ở read-store
/// (KHÔNG drop partition — D15/<c>#AUDIT-41</c>); Info/Warning drop sau 6 tháng.</para>
/// </summary>
/// 
public static class Severities
{
    /// <summary>Hành động bình thường, thành công (vd Login OK, BatteryCreated).</summary>
    public const string Info = "Info";

    /// <summary>Bất thường nhẹ / thất bại không nghiêm trọng (vd validation fail, EmailDeferred).</summary>
    public const string Warning = "Warning";

    /// <summary>Sự cố nghiêm trọng cần chú ý (vd SLA breach, anomaly Critical, account locked).</summary>
    public const string Critical = "Critical";

    /// <summary>Sự kiện liên quan an ninh — luôn giữ vĩnh viễn (vd permission grant, brute-force, GDPR redact).</summary>
    public const string Security = "Security";

    /// <summary>Toàn bộ severity hợp lệ — dùng để validate ở consumer/analyzer.</summary>
    public static readonly IReadOnlyCollection<string> All = new[] { Info, Warning, Critical, Security };

    /// <summary>Severity giữ vĩnh viễn (không bị retention drop).</summary>
    public static readonly IReadOnlyCollection<string> Permanent = new[] { Critical, Security };
}
