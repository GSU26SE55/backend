using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Audit;

namespace BatteryService.Application.CQRS.Notification.Audit;

/// <summary>
/// Notification ghi 1 entry audit BatteryService (Sprint audit #AUDIT-21/22). Publish TRONG handler (trước SaveChanges)
/// → handler ghi BatteryAuditLog + BatteryAuditOutbox cùng transaction. Actor/IP/correlation resolve từ HttpContext.
/// </summary>
public sealed record BatteryAuditTrailNotification(
    string ActionCode,
    string ActionCategory,
    string Severity,
    Guid? TargetId,
    string? TargetDisplay = null,
    bool IsSuccess = true,
    string? Reason = null,
    IReadOnlyDictionary<string, object?>? Metadata = null,
    Guid? CausationId = null
) : INotification
{
    /// <summary>Factory cho battery action (map category/severity mặc định).</summary>
    public static BatteryAuditTrailNotification For(BatteryAuditActionEnum action, Guid? targetId,
        string? targetDisplay = null, bool isSuccess = true, string? reason = null,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        var code = action.ToString();
        var severity = action is BatteryAuditActionEnum.BatteryDeleted or BatteryAuditActionEnum.StatusChanged
            ? Severities.Warning : Severities.Info;
        var category = action is BatteryAuditActionEnum.ThresholdConfigChanged
            ? AuditCategories.Configuration : AuditCategories.DataModification;
        return new BatteryAuditTrailNotification(code, category, severity, targetId, targetDisplay, isSuccess, reason, metadata);
    }

    /// <summary>Factory cho alert action (#AUDIT-31, host trong Battery — D14).</summary>
    public static BatteryAuditTrailNotification ForAlert(AlertAuditActionEnum action, Guid? targetId,
        string? targetDisplay = null, bool isSuccess = true, string? reason = null,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        var code = action.ToString();
        var severity = action is AlertAuditActionEnum.AlertSeverityOverridden ? Severities.Security : Severities.Info;
        return new BatteryAuditTrailNotification(code, AuditCategories.Configuration, severity, targetId, targetDisplay, isSuccess, reason, metadata);
    }
}
