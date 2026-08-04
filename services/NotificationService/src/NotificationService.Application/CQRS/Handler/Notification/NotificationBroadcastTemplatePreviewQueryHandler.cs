using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.CQRS.Query.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Templates;

namespace NotificationService.Application.CQRS.Handler.Notification;

/// <summary>
/// Dựng nội dung xem trước cho từng kênh, bằng <b>đúng cách dispatcher sẽ làm lúc gửi thật</b>.
///
/// <para>Bài học đắt nhất của Sprint 6.5: màn hình xem trước cũ nhận dữ liệu mẫu do chính client gõ,
/// nên "xem trước thấy đúng nhưng gửi đi lại khác" — đó là cách bộ mẫu sai tên biến sống sót hàng
/// tháng. Ở đây model được dựng theo đúng khuôn <c>NotificationDispatcher.BuildTemplateModel</c>:
/// sáu biến builtin cộng các khoá trong payload, so khớp không phân biệt hoa thường.</para>
/// </summary>
public class NotificationBroadcastTemplatePreviewQueryHandler
    : IRequestHandler<NotificationBroadcastTemplatePreviewQuery, NotificationBroadcastTemplatePreviewResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ITemplateRenderer _renderer;

    public NotificationBroadcastTemplatePreviewQueryHandler(
        INotificationUnitOfWork unitOfWork, ITemplateRenderer renderer)
    {
        _unitOfWork = unitOfWork;
        _renderer = renderer;
    }

    public async Task<NotificationBroadcastTemplatePreviewResponse> Handle(
        NotificationBroadcastTemplatePreviewQuery request, CancellationToken cancellationToken)
    {
        var channels = request.Channels.Distinct().ToList();
        if (channels.Count == 0)
        {
            return new NotificationBroadcastTemplatePreviewResponse
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = "Phải chọn ít nhất một kênh gửi.",
            };
        }

        var templates = await _unitOfWork.NotificationTemplates.GetAllAsync(false)
            .Where(t => !t.IsDeleted && t.IsActive && t.Type == request.Type)
            .ToListAsync(cancellationToken);

        var model = BuildModel(request);
        var allowed = NotificationTemplateVariables.AllowedFor(request.Type);
        var rows = new List<NotificationBroadcastChannelPreviewDto>();

        foreach (var channel in channels)
        {
            // Cùng luật chọn bản với dispatcher: version cao nhất trong cặp (Loại × Kênh).
            var template = templates
                .Where(t => t.Channel == channel)
                .OrderByDescending(t => t.Version)
                .FirstOrDefault();

            if (template is null)
            {
                rows.Add(new NotificationBroadcastChannelPreviewDto
                {
                    Channel = channel,
                    HasTemplate = false,
                    Title = request.Title,
                    Body = request.Body,
                });
                continue;
            }

            // Biến mẫu gọi mà model không có giá trị ⇒ render ra rỗng. Nói trước cho admin thấy,
            // vì sau khi gửi thì không sửa lại được.
            var missing = TemplateVariableGuard.ExtractVariables(template.TitleTemplate)
                .Concat(TemplateVariableGuard.ExtractVariables(template.BodyTemplate))
                .Where(v => !model.ContainsKey(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();

            try
            {
                var title = string.IsNullOrWhiteSpace(template.TitleTemplate)
                    ? request.Title
                    : _renderer.RenderInline(template.TitleTemplate, model);
                var body = string.IsNullOrWhiteSpace(template.BodyTemplate)
                    ? request.Body
                    : _renderer.RenderInline(template.BodyTemplate, model);

                rows.Add(new NotificationBroadcastChannelPreviewDto
                {
                    Channel = channel,
                    HasTemplate = true,
                    // Giống dispatcher: render ra rỗng thì vẫn lùi về nội dung admin gõ.
                    Title = string.IsNullOrWhiteSpace(title) ? request.Title : title,
                    Body = string.IsNullOrWhiteSpace(body) ? request.Body : body,
                    MissingVariables = missing,
                });
            }
            catch (Exception ex)
            {
                // Mẫu hỏng cú pháp KHÔNG chặn gửi (dispatcher bắt lỗi rồi rơi về inline) — xem trước
                // phải nói đúng điều đó thay vì trả 500.
                rows.Add(new NotificationBroadcastChannelPreviewDto
                {
                    Channel = channel,
                    HasTemplate = true,
                    Title = request.Title,
                    Body = request.Body,
                    MissingVariables = missing,
                    RenderError = ex.Message,
                });
            }
        }

        // Cảnh báo nhẹ khi payload khai biến không thuộc loại này — không chặn (validate của lệnh
        // gửi mới chặn), nhưng nói ra để admin sửa trước khi bấm gửi.
        var unknownKeys = model.Keys
            .Where(k => !allowed.Contains(k))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new NotificationBroadcastTemplatePreviewResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = unknownKeys.Count == 0
                ? null
                : $"Payload có biến không thuộc loại thông báo này: {string.Join(", ", unknownKeys)}.",
            Data = rows,
        };
    }

    /// <summary>
    /// Dựng đúng khuôn <c>NotificationDispatcher.BuildTemplateModel</c>: sáu biến builtin cộng mọi
    /// khoá trong payload, từ điển không phân biệt hoa thường.
    /// </summary>
    private static Dictionary<string, object?> BuildModel(NotificationBroadcastTemplatePreviewQuery request)
    {
        var model = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"] = request.Title,
            ["Body"] = request.Body,
            ["EntityType"] = null,
            ["EntityId"] = null,
            ["UserId"] = request.ActorUserId,
            ["CreatedAt"] = default(DateTime),
        };

        if (string.IsNullOrWhiteSpace(request.PayloadJson))
            return model;

        try
        {
            var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.PayloadJson);
            if (payload is null)
                return model;

            foreach (var (key, value) in payload)
                model[key] = JsonElementToObject(value);
        }
        catch (JsonException)
        {
            // Payload hỏng đã bị chặn ở lệnh gửi; xem trước thì cứ hiện phần builtin.
        }

        return model;
    }

    private static object? JsonElementToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => element.ToString(),
    };
}
