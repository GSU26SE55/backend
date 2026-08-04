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

public class NotificationTemplateCreateCommandHandler
    : IRequestHandler<NotificationTemplateCreateCommand, NotificationTemplateActionResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ITemplateRenderer _renderer;
    private readonly INotificationAuditWriter _auditWriter;
    private readonly ILogger<NotificationTemplateCreateCommandHandler> _logger;

    public NotificationTemplateCreateCommandHandler(
        INotificationUnitOfWork unitOfWork,
        ITemplateRenderer renderer,
        INotificationAuditWriter auditWriter,
        ILogger<NotificationTemplateCreateCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _renderer = renderer;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<NotificationTemplateActionResponse> Handle(
        NotificationTemplateCreateCommand request, CancellationToken cancellationToken)
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

        // Cú pháp đúng chưa đủ: {{ticketCode}} compile hoàn hảo nhưng consumer ghi khoá `code`, nên
        // biến render ra rỗng và người nhận đọc phải câu cụt mà không log nào ghi lại. Đây là điểm
        // cuối cùng còn chặn được.
        var variableError = TemplateVariableGuard.FindUnknownVariables(request.Type, title, body);
        if (variableError is not null)
        {
            return new NotificationTemplateActionResponse
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = variableError,
            };
        }

        var samePair = _unitOfWork.NotificationTemplates.GetAllAsync()
            .Where(t => t.Type == request.Type && t.Channel == request.Channel);

        var alreadyExists = await samePair.AnyAsync(t => !t.IsDeleted, cancellationToken);
        if (alreadyExists)
        {
            return new NotificationTemplateActionResponse
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Cặp (loại × kênh) này đã có template. Dùng chức năng sửa để tạo phiên bản mới.",
            };
        }

        // Version phải tính trên CẢ bản đã xoá mềm: unique index (type, channel, version) KHÔNG lọc
        // is_deleted, nên dùng lại số version của một bản đã xoá sẽ vi phạm khoá.
        var maxVersion = await samePair
            .Select(t => (int?)t.Version)
            .MaxAsync(cancellationToken) ?? 0;

        var entity = new TemplateEntity
        {
            Id = Guid.NewGuid(),
            Type = request.Type,
            Channel = request.Channel,
            TitleTemplate = title,
            BodyTemplate = body,
            Version = maxVersion + 1,
            IsActive = true,
        };

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            await _unitOfWork.NotificationTemplates.AddAsync(entity);

            await _auditWriter.WriteAsync(
                NotificationAuditActionEnum.TemplateCreated,
                entity.Id,
                request.ActorUserId,
                isSuccess: true,
                reason: "Tạo template",
                metadata: new Dictionary<string, object?>
                {
                    ["type"] = entity.Type.ToString(),
                    ["channel"] = entity.Channel.ToString(),
                    ["version"] = entity.Version,
                },
                ct: cancellationToken);

            await _unitOfWork.CommitTransactionAsync();
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            _logger.LogError(ex, "Tạo template {Type}/{Channel} thất bại.", request.Type, request.Channel);

            return new NotificationTemplateActionResponse
            {
                IsSuccess = false,
                StatusCode = 500,
                Message = "Không tạo được template.",
            };
        }

        _logger.LogInformation(
            "Đã tạo template {Type}/{Channel} v{Version} (id {Id}).",
            entity.Type, entity.Channel, entity.Version, entity.Id);

        return new NotificationTemplateActionResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Đã tạo template.",
            Data = entity.Id,
        };
    }
}
