using MediatR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.CQRS.Handler.Notification;

/// <summary>Sprint 6.3 NOTI3-14 (#714).</summary>
public class MarkNotificationOpenedCommandHandler
    : IRequestHandler<MarkNotificationOpenedCommand, NotificationActionResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly INotificationAuditWriter _auditWriter;

    public MarkNotificationOpenedCommandHandler(
        INotificationUnitOfWork unitOfWork,
        INotificationAuditWriter auditWriter)
    {
        _unitOfWork = unitOfWork;
        _auditWriter = auditWriter;
    }

    public async Task<NotificationActionResponse> Handle(
        MarkNotificationOpenedCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.Notifications.GetAllAsync()
            .FirstOrDefaultAsync(
                n => n.Id == request.Id && n.UserId == request.UserId && !n.IsDeleted,
                cancellationToken);

        // Không tồn tại HOẶC của người khác → 404 (không leak existence, tránh IDOR).
        if (entity is null)
        {
            return new NotificationActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy notification."
            };
        }

        // Idempotent — client mobile có thể gửi lại khi mạng chập chờn.
        if (entity.Status == NotificationStatusEnum.Opened)
        {
            return new NotificationActionResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Notification đã được đánh dấu đã mở.",
                Data = entity.Id
            };
        }

        var now = DateTime.UtcNow;

        entity.Status = NotificationStatusEnum.Opened;
        // Mở tức là đã đọc — nếu trước đó chưa có mốc đọc thì ghi luôn, để badge và feed thống nhất.
        entity.ReadAt ??= now;
        _unitOfWork.Notifications.UpdateAsync(entity);

        // Lan sang các record anh em của cùng sự kiện (các channel khác) — cùng lý do như mark-read:
        // user đã mở rồi thì đừng gửi thêm push/email cho đúng việc đó nữa.
        // Cộng/trừ có bảo vệ tràn: record cũ hoặc record test có thể mang CreatedAt = DateTime.MinValue.
        var siblingWindow = TimeSpan.FromMinutes(1);
        var siblingWindowStart = entity.CreatedAt - DateTime.MinValue >= siblingWindow
            ? entity.CreatedAt - siblingWindow
            : DateTime.MinValue;
        var siblingWindowEnd = DateTime.MaxValue - entity.CreatedAt >= siblingWindow
            ? entity.CreatedAt + siblingWindow
            : DateTime.MaxValue;

        var siblings = await _unitOfWork.Notifications.GetAllAsync()
            .Where(n => n.Id != entity.Id
                        && n.UserId == entity.UserId
                        && !n.IsDeleted
                        && n.Type == entity.Type
                        && n.EntityType == entity.EntityType
                        && n.EntityId == entity.EntityId
                        && n.CreatedAt >= siblingWindowStart
                        && n.CreatedAt <= siblingWindowEnd
                        && (n.Status == NotificationStatusEnum.Pending
                            || n.Status == NotificationStatusEnum.Sent
                            || n.Status == NotificationStatusEnum.Delivered))
            .ToListAsync(cancellationToken);

        foreach (var sibling in siblings)
        {
            sibling.Status = NotificationStatusEnum.Read;
            sibling.ReadAt ??= now;
            sibling.NextAttemptAt = null;
            _unitOfWork.Notifications.UpdateAsync(sibling);
        }

        await _auditWriter.WriteAsync(
            NotificationAuditActionEnum.PushOpened,
            entity.Id,
            entity.UserId,
            isSuccess: true,
            reason: null,
            metadata: new Dictionary<string, object?>
            {
                ["channel"] = entity.Channel.ToString(),
                ["type"] = entity.Type.ToString(),
                ["siblingsMarkedRead"] = siblings.Count,
            },
            ct: cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new NotificationActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đánh dấu đã mở thành công.",
            Data = entity.Id
        };
    }
}
