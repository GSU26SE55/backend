using MediatR;
using SharedContracts.Audit;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Notification.Audit;

/// <summary>
/// Notification ghi 1 entry audit TicketService (Sprint audit #AUDIT-25/26). Hỗ trợ <c>CausationId</c> cho
/// causation chain (#AUDIT-27 — ticket auto-tạo từ anomaly). Publish TRONG handler trước SaveChanges (atomic).
/// </summary>
public record TicketAuditTrailNotification(
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
    /// <summary>Factory cho ticket action (map category/severity mặc định).</summary>
    public static TicketAuditTrailNotification For(TicketAuditActionEnum action, Guid? targetId,
        string? targetDisplay = null, bool isSuccess = true, string? reason = null,
        IReadOnlyDictionary<string, object?>? metadata = null, Guid? causationId = null)
    {
        var code = action.ToString();
        var severity = action switch
        {
            TicketAuditActionEnum.SlaBreached or TicketAuditActionEnum.EscalatedToManager
                or TicketAuditActionEnum.EscalatedToAdmin => Severities.Critical,
            TicketAuditActionEnum.PriorityChanged or TicketAuditActionEnum.RejectedByManager => Severities.Security,
            _ => Severities.Info,
        };
        var category = action switch
        {
            TicketAuditActionEnum.StateTransitioned or TicketAuditActionEnum.PriorityChanged
                or TicketAuditActionEnum.AssignedToStaff or TicketAuditActionEnum.UnassignedFromStaff => AuditCategories.DataModification,
            TicketAuditActionEnum.SlaBreached or TicketAuditActionEnum.EscalatedToManager
                or TicketAuditActionEnum.EscalatedToAdmin => AuditCategories.Security,
            _ => AuditCategories.DataModification,
        };
        return new TicketAuditTrailNotification(code, category, severity, targetId, targetDisplay, isSuccess, reason, metadata, causationId);
    }
}
