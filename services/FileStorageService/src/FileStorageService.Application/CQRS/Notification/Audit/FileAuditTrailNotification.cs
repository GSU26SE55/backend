using FileStorageService.Domain.Enums;
using MediatR;
using SharedContracts.Audit;

namespace FileStorageService.Application.CQRS.Notification.Audit;

/// <summary>
/// #46 QA solars.io.vn 2026-08-29 — hạ tầng FileAuditLog (entity, migration, outbox, relay,
/// endpoint đọc, trang admin) đã có ĐỦ từ Sprint audit #AUDIT-29 nhưng KHÔNG handler nào từng tạo
/// <c>new FileAuditLog</c> ⇒ trang "GDPR / data leak investigation" vĩnh viễn rỗng.
///
/// Notification ghi 1 entry audit FileStorageService, theo cùng pattern BatteryAuditTrailNotification /
/// TicketAuditTrailNotification (Sprint audit #AUDIT-21/25). Publish TRƯỚC SaveChangesAsync để ghi
/// FileAuditLog + FileAuditOutbox cùng transaction với thao tác nghiệp vụ. Actor/IP resolve từ HttpContext.
/// </summary>
public sealed record FileAuditTrailNotification(
    string ActionCode,
    string ActionCategory,
    string Severity,
    Guid? TargetId,
    string? TargetDisplay = null,
    bool IsSuccess = true,
    string? Reason = null,
    IReadOnlyDictionary<string, object?>? Metadata = null
) : INotification
{
    /// <summary>Factory cho action FileStorageService (map category/severity mặc định).</summary>
    public static FileAuditTrailNotification For(FileAuditActionEnum action, Guid? targetId,
        string? targetDisplay = null, bool isSuccess = true, string? reason = null,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        var code = action.ToString();
        var category = action switch
        {
            FileAuditActionEnum.FileDownloaded or FileAuditActionEnum.PresignedUrlGenerated
                or FileAuditActionEnum.PresignedUrlRevoked => AuditCategories.DataAccess,
            FileAuditActionEnum.AccessDenied => AuditCategories.Security,
            _ => AuditCategories.DataModification,
        };
        var severity = action == FileAuditActionEnum.AccessDenied ? Severities.Warning : Severities.Info;
        return new FileAuditTrailNotification(code, category, severity, targetId, targetDisplay, isSuccess, reason, metadata);
    }
}
