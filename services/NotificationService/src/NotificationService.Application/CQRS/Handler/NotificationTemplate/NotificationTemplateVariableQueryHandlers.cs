using MediatR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.CQRS.Query.NotificationTemplate;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Templates;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.CQRS.Handler.NotificationTemplate;

/// <summary>
/// Trả danh mục biến hợp lệ. Thuần tính toán từ <see cref="NotificationTemplateVariables"/>, không
/// chạm DB — nhưng vẫn đi qua MediatR để controller giữ đúng khuôn "chỉ gọi _mediator.Send".
/// </summary>
public class NotificationTemplateVariableListQueryHandler
    : IRequestHandler<NotificationTemplateVariableListQuery, NotificationTemplateVariableListResponse>
{
    public Task<NotificationTemplateVariableListResponse> Handle(
        NotificationTemplateVariableListQuery request, CancellationToken cancellationToken)
    {
        var builtin = NotificationTemplateVariables.Builtin
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var groups = Enum.GetValues<NotificationTypeEnum>()
            .Distinct()
            .OrderBy(t => (int)t)
            .Select(type => new NotificationTemplateVariableGroupDto
            {
                Type = type,
                TypeName = type.ToString(),
                Builtin = builtin,
                Payload = NotificationTemplateVariables.PayloadKeysFor(type).ToList(),
            })
            .ToList();

        return Task.FromResult(new NotificationTemplateVariableListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = groups,
        });
    }
}

/// <summary>
/// Dựng bảng độ phủ từ <b>dữ liệu thật</b>: gom các cặp (loại × kênh) đã sinh thông báo, đối chiếu
/// với template đang hoạt động.
///
/// <para>Vì sao lấy theo thông báo đã sinh chứ không theo ma trận cấu hình: hai thứ này từng lệch
/// nhau. Consumer pin gửi bằng <c>AllChannels</c> (có SMS) trong khi ma trận không khai SMS, nên
/// 98 tin SMS đã gửi đi mà không template nào phủ. Chỉ dữ liệu thật mới lộ ra khoảng trống đó.</para>
/// </summary>
public class NotificationTemplateCoverageQueryHandler
    : IRequestHandler<NotificationTemplateCoverageQuery, NotificationTemplateCoverageResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public NotificationTemplateCoverageQueryHandler(INotificationUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<NotificationTemplateCoverageResponse> Handle(
        NotificationTemplateCoverageQuery request, CancellationToken cancellationToken)
    {
        var produced = await _unitOfWork.Notifications.GetAllAsync(false)
            .Where(n => !n.IsDeleted)
            .GroupBy(n => new { n.Type, n.Channel })
            .Select(g => new
            {
                g.Key.Type,
                g.Key.Channel,
                Count = g.Count(),
            })
            .ToListAsync(cancellationToken);

        var templates = await _unitOfWork.NotificationTemplates.GetAllAsync(false)
            .Where(t => !t.IsDeleted && t.IsActive)
            .Select(t => new { t.Type, t.Channel, t.TitleTemplate, t.BodyTemplate })
            .ToListAsync(cancellationToken);

        var byKey = templates.ToLookup(t => (t.Type, t.Channel));

        var rows = produced
            .Select(p =>
            {
                var template = byKey[(p.Type, p.Channel)].FirstOrDefault();

                // Dựng tập biến hợp lệ MỘT lần cho mỗi ô — gọi AllowedFor bên trong Where sẽ dựng
                // lại HashSet cho từng biến một.
                var allowed = NotificationTemplateVariables.AllowedFor(p.Type);

                var unknown = template is null
                    ? new List<string>()
                    : TemplateVariableGuard
                        .ExtractVariables(template.TitleTemplate)
                        .Concat(TemplateVariableGuard.ExtractVariables(template.BodyTemplate))
                        .Where(v => !allowed.Contains(v))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                return new NotificationTemplateCoverageDto
                {
                    Type = p.Type,
                    TypeName = p.Type.ToString(),
                    Channel = p.Channel,
                    NotificationCount = p.Count,
                    HasActiveTemplate = template is not null,
                    UnknownVariables = unknown,
                };
            })
            // Thiếu template xếp trước, rồi tới template có biến hỏng, rồi theo lượng thông báo —
            // để thứ cần sửa nhất nằm ngay đầu bảng.
            .OrderBy(r => r.HasActiveTemplate)
            .ThenByDescending(r => r.UnknownVariables.Count > 0)
            .ThenByDescending(r => r.NotificationCount)
            .ToList();

        return new NotificationTemplateCoverageResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = rows,
        };
    }
}
