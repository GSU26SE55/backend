using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.NotificationTemplate;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.CQRS.Handler.NotificationTemplate;

public class NotificationTemplateActivateCommandHandler
    : IRequestHandler<NotificationTemplateActivateCommand, NotificationTemplateActionResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly INotificationAuditWriter _auditWriter;
    private readonly ILogger<NotificationTemplateActivateCommandHandler> _logger;

    public NotificationTemplateActivateCommandHandler(
        INotificationUnitOfWork unitOfWork,
        INotificationAuditWriter auditWriter,
        ILogger<NotificationTemplateActivateCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<NotificationTemplateActionResponse> Handle(
        NotificationTemplateActivateCommand request, CancellationToken cancellationToken)
    {
        var target = await _unitOfWork.NotificationTemplates.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.Id && !t.IsDeleted, cancellationToken);

        if (target is null)
        {
            return new NotificationTemplateActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Template not found.",
            };
        }

        if (target.IsActive)
        {
            // Idempotent: đã là bản đang dùng thì không có gì để làm, và cũng không ghi audit rác.
            return new NotificationTemplateActionResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = $"Version {target.Version} is already the active version.",
                Data = target.Id,
            };
        }

        var previousActive = await _unitOfWork.NotificationTemplates.GetAllAsync()
            .FirstOrDefaultAsync(
                t => !t.IsDeleted
                     && t.IsActive
                     && t.Type == target.Type
                     && t.Channel == target.Channel,
                cancellationToken);

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            // Tắt bản cũ TRƯỚC, ở một lần lưu riêng — cùng lý do như ở lệnh sửa: partial unique index
            // `ux_notification_templates_active_per_key` không deferrable, bật bản mới trước khi tắt
            // bản cũ là vi phạm khoá ngay tại câu lệnh đó. Thứ tự UPDATE trong một lần SaveChanges do
            // EF quyết định, không đảm bảo được.
            if (previousActive is not null)
            {
                previousActive.IsActive = false;
                _unitOfWork.NotificationTemplates.UpdateAsync(previousActive);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            target.IsActive = true;
            _unitOfWork.NotificationTemplates.UpdateAsync(target);

            await _auditWriter.WriteAsync(
                NotificationAuditActionEnum.TemplateActivated,
                target.Id,
                request.ActorUserId,
                isSuccess: true,
                reason: "Roll back template version",
                metadata: new Dictionary<string, object?>
                {
                    ["type"] = target.Type.ToString(),
                    ["channel"] = target.Channel.ToString(),
                    ["fromVersion"] = previousActive?.Version,
                    ["toVersion"] = target.Version,
                },
                ct: cancellationToken);

            await _unitOfWork.CommitTransactionAsync();
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            _logger.LogError(ex, "Kích hoạt template {TemplateId} thất bại.", request.Id);

            return new NotificationTemplateActionResponse
            {
                IsSuccess = false,
                StatusCode = 500,
                Message = "Failed to activate the version.",
            };
        }

        _logger.LogWarning(
            "Template {Type}/{Channel} chuyển sang phiên bản {Version} (từ v{From}).",
            target.Type, target.Channel, target.Version, previousActive?.Version);

        return new NotificationTemplateActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Activated version {target.Version}.",
            Data = target.Id,
        };
    }
}
