using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.NotificationTemplate;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Application.Templates;
using NotificationService.Domain.Enums;
using TemplateEntity = NotificationService.Domain.Entities.NotificationTemplate;

namespace NotificationService.Application.CQRS.Handler.NotificationTemplate;

public class NotificationTemplateReviseCommandHandler
    : IRequestHandler<NotificationTemplateReviseCommand, NotificationTemplateActionResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ITemplateRenderer _renderer;
    private readonly INotificationAuditWriter _auditWriter;
    private readonly ILogger<NotificationTemplateReviseCommandHandler> _logger;

    public NotificationTemplateReviseCommandHandler(
        INotificationUnitOfWork unitOfWork,
        ITemplateRenderer renderer,
        INotificationAuditWriter auditWriter,
        ILogger<NotificationTemplateReviseCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _renderer = renderer;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<NotificationTemplateActionResponse> Handle(
        NotificationTemplateReviseCommand request, CancellationToken cancellationToken)
    {
        var title = request.TitleTemplate.Trim();
        var body = request.BodyTemplate.Trim();

        var syntaxError = TemplateSyntaxGuard.FindSyntaxError(_renderer, title, body);
        if (syntaxError is not null)
        {
            return new NotificationTemplateActionResponse
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = syntaxError,
            };
        }

        var source = await _unitOfWork.NotificationTemplates.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.Id && !t.IsDeleted, cancellationToken);

        if (source is null)
        {
            return new NotificationTemplateActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Template not found.",
            };
        }

        // Kiểm tên biến phải nằm SAU khi nạp source: type của phiên bản mới lấy theo bản gốc, người
        // sửa không truyền type lên. Cú pháp thì kiểm được sớm hơn vì không phụ thuộc type.
        var variableError = TemplateVariableGuard.FindUnknownVariables(source.Type, title, body);
        if (variableError is not null)
        {
            return new NotificationTemplateActionResponse
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = variableError,
            };
        }

        var siblings = await _unitOfWork.NotificationTemplates.GetAllAsync()
            .Where(t => t.Type == source.Type && t.Channel == source.Channel)
            .ToListAsync(cancellationToken);

        var nextVersion = siblings.Max(t => t.Version) + 1;
        var currentActive = siblings.FirstOrDefault(t => t.IsActive && !t.IsDeleted);

        var revision = new TemplateEntity
        {
            Id = Guid.NewGuid(),
            Type = source.Type,
            Channel = source.Channel,
            TitleTemplate = title,
            BodyTemplate = body,
            Version = nextVersion,
            IsActive = true,
        };

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            // PHẢI tắt bản đang dùng TRƯỚC, ở một lần lưu riêng.
            //
            // Index `ux_notification_templates_active_per_key` là partial unique trên (type, channel)
            // với filter `is_active AND NOT is_deleted`, và KHÔNG deferrable — Postgres kiểm ngay ở
            // từng câu lệnh. Nếu để EF gộp INSERT bản mới và UPDATE tắt bản cũ vào một lần
            // SaveChanges, thứ tự câu lệnh do EF quyết định: INSERT chạy trước là vi phạm khoá ngay.
            if (currentActive is not null)
            {
                currentActive.IsActive = false;
                _unitOfWork.NotificationTemplates.UpdateAsync(currentActive);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            await _unitOfWork.NotificationTemplates.AddAsync(revision);

            await _auditWriter.WriteAsync(
                NotificationAuditActionEnum.TemplateRevised,
                revision.Id,
                request.ActorUserId,
                isSuccess: true,
                reason: "Edit template — create new version",
                metadata: new Dictionary<string, object?>
                {
                    ["type"] = revision.Type.ToString(),
                    ["channel"] = revision.Channel.ToString(),
                    ["fromVersion"] = currentActive?.Version,
                    ["toVersion"] = revision.Version,
                },
                ct: cancellationToken);

            await _unitOfWork.CommitTransactionAsync();
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            _logger.LogError(ex, "Sửa template {TemplateId} thất bại.", request.Id);

            return new NotificationTemplateActionResponse
            {
                IsSuccess = false,
                StatusCode = 500,
                Message = "Failed to update template.",
            };
        }

        _logger.LogWarning(
            "Template {Type}/{Channel} sang phiên bản mới v{Version} (từ v{From}).",
            revision.Type, revision.Channel, revision.Version, currentActive?.Version);

        return new NotificationTemplateActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Created version {revision.Version} and activated it.",
            Data = revision.Id,
        };
    }
}
