using NotificationService.Application.Services;
using NotificationService.Domain.Enums;

namespace NotificationService.UnitTests.Helpers;

/// <summary>
/// Sprint 6.2 NOTI-13 (#684) — audit writer không làm gì, dùng cho unit test của handler/dispatcher
/// vốn không quan tâm tới audit. Test nào cần kiểm chứng audit thì dùng Mock riêng.
/// </summary>
public class NoopAuditWriter : INotificationAuditWriter
{
    public static readonly NoopAuditWriter Instance = new();

    public List<(NotificationAuditActionEnum Action, Guid NotificationId, bool IsSuccess)> Written { get; } = new();

    public Task WriteAsync(
        NotificationAuditActionEnum action,
        Guid notificationId,
        Guid userId,
        bool isSuccess,
        string? reason = null,
        IReadOnlyDictionary<string, object?>? metadata = null,
        CancellationToken ct = default)
    {
        Written.Add((action, notificationId, isSuccess));
        return Task.CompletedTask;
    }
}
