using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Domain.Enums;
using SharedContracts.Common.Requests;

namespace NotificationService.Application.CQRS.Query.Notification;

/// <summary>
/// Lấy danh sách notification của 1 user (mặc định là user hiện tại — set từ controller).
/// Hỗ trợ filter theo Type, Channel, Status.
///
/// Sprint 6.3 NOTI3-01 (#701) — MẶC ĐỊNH CHỈ TRẢ <c>Channel = InApp</c>.
/// Lý do: 1 sự kiện nghiệp vụ được ghi thành nhiều record, mỗi channel một record (đó là đơn vị
/// GIAO NHẬN, không phải đơn vị hiển thị). Trước sprint này endpoint trả hết mọi channel nên user
/// thấy cùng một thông báo lặp 2–4 lần (chat 2, Battery Critical 4, SLA P1 4) và badge chưa đọc
/// phồng đúng bấy nhiêu lần.
///
/// Vẫn giữ tham số <see cref="Channel"/>: client cần soi riêng một kênh (vd màn hình debug/admin)
/// thì truyền tường minh, khi đó bộ lọc mặc định không áp dụng.
/// </summary>
public class GetNotificationsQuery : PaginationRequest, IRequest<NotificationListResponse>
{
    public Guid UserId { get; set; }
    public NotificationTypeEnum? Type { get; set; }

    /// <summary>Null = chỉ lấy <c>InApp</c> (feed). Truyền giá trị để xem riêng 1 kênh giao nhận.</summary>
    public NotificationChannelEnum? Channel { get; set; }

    public NotificationStatusEnum? Status { get; set; }

    /// <summary>Chỉ lấy notification chưa đọc (Status != Read).</summary>
    public bool? UnreadOnly { get; set; }

    /// <summary>
    /// True = bỏ bộ lọc feed, trả record của MỌI channel (dùng cho màn hình chẩn đoán).
    /// Mặc định false — người dùng cuối luôn chỉ nên thấy feed.
    /// </summary>
    public bool IncludeAllChannels { get; set; }
}
