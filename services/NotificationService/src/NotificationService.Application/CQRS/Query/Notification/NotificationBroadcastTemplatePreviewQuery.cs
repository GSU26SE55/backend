using System.Text.Json.Serialization;
using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.CQRS.Query.Notification;

/// <summary>
/// 03/08/2026 — xem trước nội dung một lần gửi hàng loạt <b>khi bật "dùng mẫu"</b>, dựng riêng cho
/// TỪNG KÊNH.
///
/// <para><b>Vì sao phải tách theo kênh:</b> mẫu khoá theo cặp <c>(Loại × Kênh)</c> và bản SMS được
/// nén ngắn lại (tính tiền theo đoạn), nên cùng một lần gửi 3 kênh cho ra 3 nội dung khác nhau. Một
/// ô xem trước duy nhất sẽ nói dối về 2 trong 3 kênh.</para>
///
/// <para><b>Vì sao không tái dùng preview của trang mẫu:</b> endpoint đó nhận <c>id</c> của một mẫu
/// và dữ liệu mẫu do client tự gõ. Ở đây admin chưa chọn mẫu nào — mẫu được suy ra từ (Loại × Kênh)
/// đang chọn — và dữ liệu là biến thật sẽ gửi đi.</para>
/// </summary>
public class NotificationBroadcastTemplatePreviewQuery : IRequest<NotificationBroadcastTemplatePreviewResponse>
{
    public NotificationTypeEnum Type { get; set; }

    public List<NotificationChannelEnum> Channels { get; set; } = new();

    /// <summary>Nội dung dự phòng — hiện đúng thứ sẽ gửi ở kênh không có mẫu khớp.</summary>
    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>Giá trị các biến, dạng JSON object. Bỏ trống ⇒ mọi biến render ra rỗng.</summary>
    public string? PayloadJson { get; set; }

    [JsonIgnore]
    public Guid ActorUserId { get; set; }
}
