using AuthService.Domain.Enums;
using MediatR;

namespace AuthService.Application.CQRS.Notification.Login;

/// <summary>
/// Notification fire-and-forget để ghi 1 login attempt vào DB.
///
/// Quy tắc dùng (giống <see cref="Audit.AuditTrailNotification"/>):
/// - Publish TRƯỚC SaveChangesAsync để atomic với business transaction.
/// - IP / UserAgent / DeviceId được handler tự resolve từ HttpContext.
/// </summary>
/// <param name="AccountId">Id account nếu tìm được, null nếu email không tồn tại.</param>
/// <param name="AttemptedEmail">Email mà client submit (normalized).</param>
/// <param name="Result">Kết quả attempt.</param>
/// <param name="Method">"Password", "Google", "VerifyOtp".</param>
/// <param name="Note">Note bổ sung (vd: số lần thử còn lại).</param>
public sealed record LoginAttemptNotification(
    Guid? AccountId,
    string AttemptedEmail,
    LoginAttemptResult Result,
    string Method,
    string? Note = null
) : INotification;
