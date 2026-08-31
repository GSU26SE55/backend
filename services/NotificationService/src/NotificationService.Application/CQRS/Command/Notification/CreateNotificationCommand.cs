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
    /// <summary>
    /// Người nhận notification (AccountId).
    ///
    /// <para>
    /// <b>Sửa 30/07/2026 — gỡ <c>[JsonIgnore]</c>.</b> Trường này từng bị đánh <c>[JsonIgnore]</c>
    /// (sao chép nhầm từ <c>MarkNotificationReadCommand</c>, nơi UserId lấy từ claim JWT), trong khi
    /// <c>NotificationsController.Create</c> KHÔNG hề gán nó từ token. Hệ quả:
    /// <c>POST /api/notifications</c> luôn tạo bản ghi với <c>UserId = Guid.Empty</c> ⇒ dispatch worker
    /// đánh <c>Failed</c> với lý do <c>empty_user_id</c>, endpoint vô dụng cho đúng mục đích mà tài liệu
    /// của chính nó mô tả ("test, backfill thủ công"). Phát hiện khi test E2E 30/07/2026.
    /// </para>
    /// <para>
    /// Gỡ <c>[JsonIgnore]</c> KHÔNG ảnh hưởng 9 consumer đang dùng command này: chúng gán UserId bằng
    /// code C# (<c>_mediator.Send(new CreateNotificationCommand { UserId = ... })</c>), mà thuộc tính
    /// này chỉ chi phối việc deserialize JSON.
    /// </para>
    /// <para>
    /// An toàn: endpoint gọi nó là <c>[Authorize(Roles = "Admin")]</c> — chỉ Admin chỉ định được
    /// người nhận, đúng vai trò.
    /// </para>
    /// </summary>
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

        // Sửa 30/07/2026 — ĐẢO NGƯỢC ghi chú GH-594 cũ ("không reject Guid.Empty vì consumer phát
        // broadcast với recipient placeholder, dispatcher resolve sau").
        //
        // Thiết kế đó KHÔNG tồn tại trong code: đã rà đủ 9 consumer dùng command này — tất cả đều
        // resolve recipient THẬT trước khi gửi (qua IRecipientResolver hoặc id lấy thẳng từ event),
        // và ChatCreatedConsumer còn chặn tường minh `recipientId == Guid.Empty`. Không đường nào
        // phát placeholder cả. Dispatcher (Sprint 6.2) cũng không resolve broadcast — nó đánh Failed
        // ngay với lý do `empty_user_id`.
        //
        // Vì vậy bản ghi UserId rỗng là bản ghi CHẮC CHẮN thất bại. Từ chối sớm kèm thông báo rõ
        // ràng tốt hơn nhiều so với tạo ra một dòng rác rồi để worker đánh Failed.
        // Consumer chỉ log warning khi Send() trả IsSuccess=false (đã kiểm) ⇒ không gây vòng retry.
        if (UserId == Guid.Empty)
        {
            response.ListErrors.Add(new Errors
            {
                Field = "UserId",
                Detail = "UserId is required — a notification without a recipient can never be sent.",
            });
        }

        if (!Enum.IsDefined(typeof(NotificationTypeEnum), Type))
            response.ListErrors.Add(new Errors { Field = "Type", Detail = "Invalid Type." });

        if (!Enum.IsDefined(typeof(NotificationChannelEnum), Channel))
            response.ListErrors.Add(new Errors { Field = "Channel", Detail = "Invalid Channel." });

        if (string.IsNullOrWhiteSpace(Title))
            response.ListErrors.Add(new Errors { Field = "Title", Detail = "Title is required." });
        // Trim trước khi đo — FE gửi giá trị đã trim, đo raw sẽ lệch ở khoảng trắng cuối.
        else if (Title.Trim().Length > 200)
            response.ListErrors.Add(new Errors { Field = "Title", Detail = "Title must be at most 200 characters." });

        if (string.IsNullOrWhiteSpace(Body))
            response.ListErrors.Add(new Errors { Field = "Body", Detail = "Body is required." });
        else if (Body.Trim().Length > 2000)
            response.ListErrors.Add(new Errors { Field = "Body", Detail = "Body must be at most 2000 characters." });

        if (!string.IsNullOrEmpty(EntityType) && EntityType.Trim().Length > 100)
            response.ListErrors.Add(new Errors { Field = "EntityType", Detail = "EntityType must be at most 100 characters." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
