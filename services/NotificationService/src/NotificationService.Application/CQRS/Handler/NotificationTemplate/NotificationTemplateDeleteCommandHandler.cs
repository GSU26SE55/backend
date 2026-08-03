using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.NotificationTemplate;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.CQRS.Handler.NotificationTemplate;

public class NotificationTemplateDeleteCommandHandler
    : IRequestHandler<NotificationTemplateDeleteCommand, NotificationTemplateActionResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly INotificationAuditWriter _auditWriter;
    private readonly ILogger<NotificationTemplateDeleteCommandHandler> _logger;

    public NotificationTemplateDeleteCommandHandler(
        INotificationUnitOfWork unitOfWork,
        INotificationAuditWriter auditWriter,
        ILogger<NotificationTemplateDeleteCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<NotificationTemplateActionResponse> Handle(
        NotificationTemplateDeleteCommand request, CancellationToken cancellationToken)
    {
        var target = await _unitOfWork.NotificationTemplates.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.Id && !t.IsDeleted, cancellationToken);

        if (target is null)
        {
            return new NotificationTemplateActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy template.",
            };
        }

        // Chặn xoá bản đang dùng: cặp (loại × kênh) mất bản active thì dispatcher lặng lẽ rơi về
        // chuỗi hardcode trong consumer — thông báo vẫn gửi nhưng mất nội dung tuỳ biến, không ai hay.
        if (target.IsActive)
        {
            return new NotificationTemplateActionResponse
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Không xoá được bản đang dùng. Hãy kích hoạt một phiên bản khác trước.",
            };
        }

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            // DeleteAsync là VOID và AuditableEntityInterceptor tự chuyển thành soft delete
            // (IsDeleted = true, DeletedAt = UtcNow) — không xoá cứng, giữ lại dấu vết nội dung cũ.
            _unitOfWork.NotificationTemplates.DeleteAsync(target);

            await _auditWriter.WriteAsync(
                NotificationAuditActionEnum.TemplateDeleted,
                target.Id,
                request.ActorUserId,
                isSuccess: true,
                reason: "Xoá phiên bản template",
                metadata: new Dictionary<string, object?>
                {
                    ["type"] = target.Type.ToString(),
                    ["channel"] = target.Channel.ToString(),
                    ["version"] = target.Version,
                },
                ct: cancellationToken);

            await _unitOfWork.CommitTransactionAsync();
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            _logger.LogError(ex, "Xoá template {TemplateId} thất bại.", request.Id);

            return new NotificationTemplateActionResponse
            {
                IsSuccess = false,
                StatusCode = 500,
                Message = "Không xoá được template.",
            };
        }

        _logger.LogWarning(
            "Đã xoá template {Type}/{Channel} v{Version}.",
            target.Type, target.Channel, target.Version);

        return new NotificationTemplateActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Đã xoá phiên bản {target.Version}.",
            Data = target.Id,
        };
    }
}
