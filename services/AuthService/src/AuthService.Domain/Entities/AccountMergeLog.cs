using SharedKernels.Domain;

namespace AuthService.Domain.Entities;

/// <summary>
/// #AUTH-47: Audit row dedicated cho mỗi lần admin merge 2 account.
/// Tách khỏi AuditLog chung vì cần lưu data conflict resolution + secondary account snapshot
/// (để rollback hoặc compliance investigation sau này).
///
/// Row IMMUTABLE — không cho phép update sau khi insert (enforce qua append-only trigger
/// trong migration sau, tương tự AUTH-29).
/// </summary>
public class AccountMergeLog : AuditableEntity
{
    /// <summary>Account giữ lại (primary). Audit/sessions/profile được chuyển VỀ đây.</summary>
    public Guid PrimaryAccountId { get; set; }

    /// <summary>Account bị merge (secondary). Bị soft-delete + MergedIntoId = PrimaryAccountId.</summary>
    public Guid SecondaryAccountId { get; set; }

    /// <summary>Admin thực hiện merge.</summary>
    public Guid PerformedBy { get; set; }

    /// <summary>Lý do merge (free-text, max 1000 chars) — bắt buộc để audit/compliance.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// JSON snapshot của secondary account TRƯỚC khi merge (email/phone/fullName/googleId/roleId/...).
    /// Dùng cho compliance investigation hoặc rollback manual.
    /// </summary>
    public string SecondaryAccountSnapshotJson { get; set; } = string.Empty;

    /// <summary>JSON snapshot conflict resolution decisions (email kept, phone kept, etc).</summary>
    public string ConflictResolutionJson { get; set; } = string.Empty;

    /// <summary>Số session bị force-logout từ secondary (RefreshToken active → revoked).</summary>
    public int SessionsRevoked { get; set; }

    /// <summary>Số audit log row trace lại để link tới primary (statistic).</summary>
    public int AuditLogsLinked { get; set; }
}
