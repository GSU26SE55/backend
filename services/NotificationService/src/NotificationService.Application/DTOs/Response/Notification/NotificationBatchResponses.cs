using NotificationService.Domain.Enums;
using SharedContracts.Common.Responses;

namespace NotificationService.Application.DTOs.Response.Notification;

/// <summary>Sprint 6.4 NOTI4-07 — kết quả một lần gửi hàng loạt.</summary>
public class NotificationBroadcastDto
{
    /// <summary>Id lần gửi — dùng để mở màn hình chi tiết / thống kê.</summary>
    public Guid BatchId { get; set; }

    /// <summary>Số người nhận SAU KHI gom trùng và loại người không hoạt động.</summary>
    public int RecipientCount { get; set; }

    /// <summary>Số dòng thông báo đã sinh (= người nhận × số kênh).</summary>
    public int NotificationCount { get; set; }

    /// <summary>Số nhóm được nhắm tới và thực sự tồn tại.</summary>
    public int GroupCount { get; set; }

    /// <summary>
    /// Số id trong <c>userIds</c> bị bỏ qua vì không tìm thấy trong read-model tài khoản, hoặc tài
    /// khoản đang ngừng hoạt động. Nói ra để admin không tưởng đã gửi đủ.
    /// </summary>
    public int SkippedUsers { get; set; }
}

/// <summary>
/// Sprint 6.4 NOTI4-07 — kết quả xem trước: <b>không</b> gửi gì, chỉ trả lời "bấm gửi thì bao nhiêu
/// người nhận". Có endpoint riêng vì cộng <c>memberCount</c> của từng nhóm ở phía client là SAI —
/// người thuộc hai nhóm sẽ bị đếm hai lần.
/// </summary>
public class NotificationBroadcastPreviewDto
{
    /// <summary>Số người nhận sau khi gom trùng — con số thật sẽ nhận thông báo.</summary>
    public int RecipientCount { get; set; }

    /// <summary>Số dòng thông báo sẽ sinh ra (= <see cref="RecipientCount"/> × số kênh).</summary>
    public int NotificationCount { get; set; }

    /// <summary>
    /// Tổng số người nếu cộng dồn từng nhóm mà KHÔNG gom trùng. Lớn hơn
    /// <see cref="RecipientCount"/> nghĩa là các nhóm đang giao nhau — hiển thị chênh lệch này để
    /// admin hiểu vì sao con số nhỏ hơn tổng họ nhẩm ra.
    /// </summary>
    public int RawCount { get; set; }

    /// <summary>Số id cá nhân bị bỏ qua (không tồn tại hoặc ngừng hoạt động).</summary>
    public int SkippedUsers { get; set; }

    /// <summary>Số nhóm được nhắm tới nhưng không tìm thấy (đã xoá).</summary>
    public int MissingGroups { get; set; }
}

/// <summary>Sprint 6.4 NOTI4-09 — một lần gửi trong danh sách lịch sử.</summary>
public class NotificationBatchDto
{
    public Guid Id { get; set; }

    public NotificationTypeEnum Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>Các kênh đã nhắm tới, dạng số.</summary>
    public List<NotificationChannelEnum> Channels { get; set; } = new();

    /// <summary>1 = Event (tự động từ sự kiện) · 2 = Manual (admin bấm gửi).</summary>
    public NotificationBatchSourceEnum Source { get; set; }

    /// <summary>1 = Pending · 2 = FannedOut · 3 = Failed.</summary>
    public NotificationBatchStatusEnum Status { get; set; }

    public int RecipientCount { get; set; }

    public int NotificationCount { get; set; }

    /// <summary>Admin đã bấm gửi. <c>null</c> với lần gửi sinh tự động từ sự kiện.</summary>
    public Guid? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Sprint 6.4 NOTI4-09 — chi tiết một lần gửi kèm thống kê giao nhận.
/// Đây chính là câu hỏi trước sprint này không trả lời được vì không có khoá gom nào.
/// </summary>
public class NotificationBatchDetailDto : NotificationBatchDto
{
    /// <summary>Nhóm đã nhắm tới, kèm tên để hiển thị. Nhóm đã xoá vẫn hiện (lịch sử không mất).</summary>
    public List<NotificationBatchTargetDto> Targets { get; set; } = new();

    /// <summary>Tổng số dòng thông báo thực tế đang có trong DB thuộc lần gửi này.</summary>
    public int TotalRows { get; set; }

    /// <summary>Số người nhận riêng biệt.</summary>
    public int DistinctRecipients { get; set; }

    /// <summary>Số dòng đã giao xuống kênh thành công.</summary>
    public int SentCount { get; set; }

    /// <summary>Số dòng người nhận đã đọc.</summary>
    public int ReadCount { get; set; }

    /// <summary>Số dòng giao thất bại (kênh bị tắt, thiếu email/token, lỗi nhà cung cấp…).</summary>
    public int FailedCount { get; set; }

    /// <summary>Số dòng còn chờ worker giao.</summary>
    public int PendingCount { get; set; }
}

/// <summary>Sprint 6.4 NOTI4-09 — một mục tiêu của lần gửi.</summary>
public class NotificationBatchTargetDto
{
    /// <summary>1 = Group · 2 = User.</summary>
    public NotificationBatchTargetKindEnum TargetKind { get; set; }

    public Guid? GroupId { get; set; }

    /// <summary>
    /// Tên nhóm tại thời điểm gửi. Nhóm bị <b>xoá mềm</b> vẫn trả về ĐÚNG TÊN — truy vấn cố ý
    /// không lọc <c>IsDeleted</c>, vì lịch sử mà mất tên thì người xem chỉ còn thấy "một nhóm nào
    /// đó". Chỉ <c>null</c> khi dòng nhóm bị xoá <b>cứng</b> khỏi DB — không xảy ra qua API.
    /// </summary>
    public string? GroupName { get; set; }

    public Guid? UserId { get; set; }
}

/// <summary>
/// 03/08/2026 — nội dung một kênh sẽ nhận khi bật "dùng mẫu".
/// </summary>
public class NotificationBroadcastChannelPreviewDto
{
    public NotificationChannelEnum Channel { get; set; }

    /// <summary>
    /// <c>false</c> ⇒ cặp (Loại × Kênh) này KHÔNG có mẫu đang dùng, nên kênh đó rơi về tiêu đề/nội
    /// dung admin gõ. Nói ra để admin không tưởng cả 3 kênh đều theo mẫu.
    /// </summary>
    public bool HasTemplate { get; set; }

    /// <summary>Tiêu đề sau khi render — hoặc chính chữ admin gõ nếu kênh không có mẫu.</summary>
    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Biến mà mẫu của kênh này gọi nhưng payload không có giá trị ⇒ chỗ đó render ra rỗng.
    /// Rỗng là tốt.
    /// </summary>
    public List<string> MissingVariables { get; set; } = new();

    /// <summary>Mẫu hỏng cú pháp — kênh này sẽ rơi về nội dung dự phòng lúc gửi thật.</summary>
    public string? RenderError { get; set; }
}

/// <summary>Kết quả gửi hàng loạt.</summary>
public class NotificationBroadcastResponse : CommonResponse<NotificationBroadcastDto> { }

/// <summary>Xem trước nội dung theo từng kênh khi bật "dùng mẫu".</summary>
public class NotificationBroadcastTemplatePreviewResponse
    : CommonResponse<List<NotificationBroadcastChannelPreviewDto>>
{ }

/// <summary>Kết quả xem trước số người nhận.</summary>
public class NotificationBroadcastPreviewResponse : CommonResponse<NotificationBroadcastPreviewDto> { }

/// <summary>Một trang lịch sử gửi.</summary>
public class NotificationBatchListResponse : CommonResponse<PaginationResponse<NotificationBatchDto>> { }

/// <summary>Chi tiết một lần gửi kèm thống kê.</summary>
public class NotificationBatchDetailResponse : CommonResponse<NotificationBatchDetailDto> { }
