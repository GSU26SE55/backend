using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Query.NotificationTemplate;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Templates;

namespace NotificationService.Application.CQRS.Handler.NotificationTemplate;

public class NotificationTemplatePreviewQueryHandler
    : IRequestHandler<NotificationTemplatePreviewQuery, NotificationTemplatePreviewResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ITemplateRenderer _renderer;
    private readonly ILogger<NotificationTemplatePreviewQueryHandler> _logger;

    public NotificationTemplatePreviewQueryHandler(
        INotificationUnitOfWork unitOfWork,
        ITemplateRenderer renderer,
        ILogger<NotificationTemplatePreviewQueryHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _renderer = renderer;
        _logger = logger;
    }

    public async Task<NotificationTemplatePreviewResponse> Handle(
        NotificationTemplatePreviewQuery request, CancellationToken cancellationToken)
    {
        var template = await _unitOfWork.NotificationTemplates.GetAllAsync(false)
            .FirstOrDefaultAsync(t => t.Id == request.Id && !t.IsDeleted, cancellationToken);

        if (template is null)
        {
            return new NotificationTemplatePreviewResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy template.",
            };
        }

        // BuildFor (không phải Build) — nạp sẵn đúng khoá mà consumer của type này ghi, để biến gọi
        // sai tên hiện ra rỗng ngay trên màn hình xem trước thay vì chỉ lộ ra lúc đã gửi thật.
        var model = TemplateSampleModel.BuildFor(template.Type, request.SampleData);

        try
        {
            return new NotificationTemplatePreviewResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Data = new NotificationTemplatePreviewDto
                {
                    Type = template.Type,
                    Channel = template.Channel,
                    Version = template.Version,
                    Title = _renderer.RenderInline(template.TitleTemplate, model),
                    Body = _renderer.RenderInline(template.BodyTemplate, model),
                },
            };
        }
        catch (Exception ex)
        {
            // Template hỏng cú pháp trả 400 kèm thông báo, thay vì để GlobalExceptionMiddleware ném 500:
            // người soạn cần biết hỏng ở đâu để sửa, không phải một mã lỗi vô nghĩa.
            _logger.LogWarning(ex, "Preview template {TemplateId} lỗi cú pháp.", request.Id);

            return new NotificationTemplatePreviewResponse
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = $"Template hỏng cú pháp: {ex.Message}",
            };
        }
    }
}
