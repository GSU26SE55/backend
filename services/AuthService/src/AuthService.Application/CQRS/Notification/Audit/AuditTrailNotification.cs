using AuthService.Domain.Enums;
using MediatR;

namespace AuthService.Application.CQRS.Notification.Audit;

/// <summary>
/// Notification fire-and-forget để ghi một entry vào audit log.
///
/// Quy tắc dùng trong Command Handler:
/// - Publish notification BÊN TRONG transaction của handler (TRƯỚC SaveChangesAsync) — handler chỉ
///   Add vào DbContext, không SaveChanges → audit log + business data commit atomic.
/// - Actor (người thực hiện) được tự resolve từ <see cref="SharedInfrastructure.Services.ICurrentUserService"/>.
///   Nếu hành động là anonymous (login/register) hoặc background job, truyền <paramref name="ActorAccountIdOverride"/>.
/// - IP / UserAgent / DeviceId / CorrelationId được handler tự resolve từ HttpContext.
/// </summary>
/// <param name="Action">Loại hành động (xem <see cref="AuditActionEnum"/>).</param>
/// <param name="TargetAccountId">AccountId mục tiêu. Null nếu không xác định (vd login với email không tồn tại).</param>
/// <param name="IsSuccess">true nếu hành động thành công, false nếu bị chặn/thất bại.</param>
/// <param name="TargetEmail">Email mục tiêu (khi không có TargetAccountId).</param>
/// <param name="Reason">Lý do thất bại hoặc note bổ sung (tự động truncate 500 ký tự).</param>
/// <param name="Metadata">Dict serialize thành JSON, vd { ["oldStatus"]=1, ["newStatus"]=2 }.</param>
/// <param name="ActorAccountIdOverride">Override actor (khi anonymous flow hoặc background job).</param>
public sealed record AuditTrailNotification(
    AuditActionEnum Action,
    Guid? TargetAccountId,
    bool IsSuccess,
    string? TargetEmail = null,
    string? Reason = null,
    IReadOnlyDictionary<string, object?>? Metadata = null,
    Guid? ActorAccountIdOverride = null
) : INotification;
