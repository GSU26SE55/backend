using MediatR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.CQRS.Handler.Notification;

public class MarkNotificationReadCommandHandler : IRequestHandler<MarkNotificationReadCommand, NotificationActionResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly INotificationAuditWriter _auditWriter;   // Sprint 6.2 NOTI-13 (#684)

    public MarkNotificationReadCommandHandler(
        INotificationUnitOfWork unitOfWork,
        INotificationAuditWriter auditWriter)
    {
        _unitOfWork = unitOfWork;
        _auditWriter = auditWriter;
    }

    public async Task<NotificationActionResponse> Handle(MarkNotificationReadCommand request, CancellationToken cancellationToken)
    {
        // Tracking ON — sẽ update entity. Chỉ lấy noti thuộc về chính user (ownership).
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

        // Idempotent: đã Read rồi → vẫn 200, không ghi lại.
        // Sprint 6.3 NOTI3-14 (#714) — Opened cũng dừng ở đây: hạ Opened xuống Read là mất thông tin.
        if (entity.Status is NotificationStatusEnum.Read or NotificationStatusEnum.Opened)
        {
            return new NotificationActionResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Notification đã được đánh dấu đã đọc.",
                Data = entity.Id
            };
        }

        var now = DateTime.UtcNow;

        entity.Status = NotificationStatusEnum.Read;
        entity.ReadAt = now;
        _unitOfWork.Notifications.UpdateAsync(entity);

        // Sprint 6.3 NOTI3-01 (#701) — lan trạng thái sang các record ANH EM của cùng một sự kiện
        // (các channel khác được ghi trong cùng một lần WriteAsync).
        //
        // Vì sao cần: record Push/Email/Sms còn `Pending` sẽ bị dispatch worker gửi đi ở vòng sau
        // ⇒ user đã đọc trong app rồi vẫn lãnh thêm một cú push cho đúng việc đó. Đánh dấu Read
        // khiến worker bỏ qua chúng (worker chỉ lấy `Status = Pending`).
        //
        // Record đã `Failed` thì GIỮ NGUYÊN — đó là dữ liệu chẩn đoán, ghi đè sẽ mất dấu vết lỗi.
        // Cộng/trừ có bảo vệ tràn: record dữ liệu cũ hoặc record tạo trong test có thể mang
        // CreatedAt = DateTime.MinValue, khi đó AddMinutes(-1) ném ArgumentOutOfRangeException
        // và làm hỏng cả endpoint mark-read.
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
                            // Sprint 6.3 NOTI3-14 (#714) — record push đã Delivered cũng nên theo
                            // trạng thái đọc để badge và feed không lệch nhau.
                            || n.Status == NotificationStatusEnum.Delivered))
            .ToListAsync(cancellationToken);

        foreach (var sibling in siblings)
        {
            sibling.Status = NotificationStatusEnum.Read;
            sibling.ReadAt = now;
            sibling.NextAttemptAt = null;
            _unitOfWork.Notifications.UpdateAsync(sibling);
        }

        // Sprint 6.2 NOTI-13 (#684) — audit InAppRead, ghi cùng transaction với việc đổi trạng thái.
        await _auditWriter.WriteAsync(
            NotificationAuditActionEnum.InAppRead,
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
            Message = "Đánh dấu đã đọc thành công.",
            Data = entity.Id
        };
    }
}
