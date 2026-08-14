using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Domain.Enums;
using SharedContracts.Common.Requests;

namespace NotificationService.Application.CQRS.Query.Notification;

/// <summary>
/// Sprint 6.4 NOTI4-07 — xem trước số người nhận, KHÔNG gửi gì.
///
/// <para>Phải có endpoint riêng vì phía client cộng <c>memberCount</c> của từng nhóm là <b>sai</b>:
/// người thuộc hai nhóm cùng được chọn sẽ bị đếm hai lần. Con số hiển thị trước khi bấm gửi phải là
/// con số sau khi gom trùng, và phải do đúng đoạn logic của lần gửi thật tính ra.</para>
/// </summary>
public class NotificationBroadcastPreviewQuery : IRequest<NotificationBroadcastPreviewResponse>
{
    public List<Guid> GroupIds { get; set; } = new();

    public List<Guid> UserIds { get; set; } = new();

    /// <summary>Dùng để nhân ra số dòng sẽ sinh. Rỗng ⇒ chỉ trả số người nhận.</summary>
    public List<NotificationChannelEnum> Channels { get; set; } = new();
}

/// <summary>Sprint 6.4 NOTI4-09 — lịch sử gửi, có phân trang.</summary>
public class NotificationBatchGetListQuery : PaginationRequest, IRequest<NotificationBatchListResponse>
{
    /// <summary>Lọc theo nguồn: 1 = tự động từ sự kiện, 2 = admin bấm gửi.</summary>
    public NotificationBatchSourceEnum? Source { get; set; }

    public NotificationTypeEnum? Type { get; set; }
}

/// <summary>Sprint 6.4 NOTI4-09 — chi tiết một lần gửi kèm thống kê giao nhận.</summary>
public class NotificationBatchGetByIdQuery : IRequest<NotificationBatchDetailResponse>
{
    public Guid Id { get; set; }
}
