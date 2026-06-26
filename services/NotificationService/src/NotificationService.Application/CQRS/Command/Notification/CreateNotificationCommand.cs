using System.Text.Json.Serialization;
using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Domain.Enums;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Command.Notification;

/// <summary>
/// Tạo 1 notification record. Endpoint này chủ yếu phục vụ admin/test —
/// flow production sẽ tạo notification từ Consumer (RabbitMQ event) hoặc Dispatcher.
/// </summary>
public class CreateNotificationCommand : IRequest<NotificationActionResponse>, IValidatable<NotificationActionResponse>
{
    [JsonIgnore]
    public Guid UserId { get; set; }
    public NotificationTypeEnum Type { get; set; }
    public NotificationChannelEnum Channel { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }

    /// <summary>
    /// Sprint IoT-2 #IoT2-31 — bypass quiet hours check khi gửi cho EnvironmentalIncident Critical.
    /// Dispatcher (Sprint 6) đọc flag này → SKIP NotificationPreference.QuietHoursStart/End.
    /// Mặc định false; chỉ set true cho Critical channels (smoke/water bypass per overall.md §3.4 + §49.3).
    /// </summary>
    public bool BypassQuietHours { get; set; }

    public Task<NotificationActionResponse> ValidateAsync()
    {
        var response = new NotificationActionResponse();

        // GH-594: KHÔNG reject UserId == Guid.Empty. Consumer phát notification "broadcast"
        // với recipient placeholder Guid.Empty (recipient thật resolve sau qua dispatcher /
        // AccountSyncReadModel — Sprint 6). Reject ở đây khiến ValidationBehavior short-circuit
        // → notification không bao giờ được tạo. Vẫn validate Type/Channel/Title/Body bên dưới.

        if (!Enum.IsDefined(typeof(NotificationTypeEnum), Type))
            response.ListErrors.Add(new Errors { Field = "Type", Detail = "Type không hợp lệ." });

        if (!Enum.IsDefined(typeof(NotificationChannelEnum), Channel))
            response.ListErrors.Add(new Errors { Field = "Channel", Detail = "Channel không hợp lệ." });

        if (string.IsNullOrWhiteSpace(Title))
            response.ListErrors.Add(new Errors { Field = "Title", Detail = "Title không được trống." });
        else if (Title.Length > 200)
            response.ListErrors.Add(new Errors { Field = "Title", Detail = "Title tối đa 200 ký tự." });

        if (string.IsNullOrWhiteSpace(Body))
            response.ListErrors.Add(new Errors { Field = "Body", Detail = "Body không được trống." });
        else if (Body.Length > 2000)
            response.ListErrors.Add(new Errors { Field = "Body", Detail = "Body tối đa 2000 ký tự." });

        if (!string.IsNullOrEmpty(EntityType) && EntityType.Length > 100)
            response.ListErrors.Add(new Errors { Field = "EntityType", Detail = "EntityType tối đa 100 ký tự." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
