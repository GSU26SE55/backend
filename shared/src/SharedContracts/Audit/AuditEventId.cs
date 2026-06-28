namespace SharedContracts.Audit;

/// <summary>
/// Factory tập trung cho audit <c>event_id</c> (Sprint audit <c>#AUDIT-04</c>).
///
/// <para><b>net8:</b> <c>Guid.CreateVersion7()</c> (UUIDv7, time-ordered) chỉ có từ .NET 9 →
/// hiện fallback <see cref="System.Guid.NewGuid()"/> (v4). Khi nâng .NET 9, đổi DUY NHẤT thân hàm này
/// sang <c>Guid.CreateVersion7()</c> — mọi handler đã gọi qua đây nên không phải sửa nơi khác.</para>
///
/// <para>Mọi nơi tạo event_id PHẢI gọi <see cref="New"/> — analyzer <c>AUDIT002</c> chặn
/// <c>Guid.NewGuid()</c> trực tiếp cho event_id, gate ở stage <c>ci-rules</c>.</para>
/// </summary>
public static class AuditEventId
{
    /// <summary>Sinh event_id mới cho một audit event.</summary>
    public static System.Guid New() => System.Guid.NewGuid();
}
