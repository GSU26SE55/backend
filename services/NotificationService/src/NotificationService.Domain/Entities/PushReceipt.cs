using SharedKernels.Domain;

namespace NotificationService.Domain.Entities;

/// <summary>
/// Sprint 6.3 NOTI3-02 (#702) — biên nhận (receipt) của một message push đã gửi lên Expo.
///
/// **Vì sao cần bảng này:** Expo Push là relay bất đồng bộ. HTTP 200 + ticket <c>status:"ok"</c> chỉ
/// nghĩa là *Expo đã nhận*, KHÔNG phải *thiết bị đã nhận*. Kết quả thật chỉ có khi gọi
/// <c>POST /push/getReceipts</c> với ticket id (Expo giữ receipt 24h). Trước sprint này hệ thống vứt
/// ticket id đi ⇒ token chết vẫn nằm trong DB và mọi lần gửi sau đều lãng phí, còn
/// <c>Notification.Status = Sent</c> là một lời nói dối (§17.6.3, R-38).
///
/// Một <see cref="Notification"/> push tới người dùng có N thiết bị sẽ sinh N bản ghi receipt.
/// </summary>
public class PushReceipt : AuditableEntity
{
    /// <summary>Notification đã sinh ra message push này.</summary>
    public Guid NotificationId { get; set; }

    /// <summary>User nhận — nhân bản để đối soát không phải join sang Notification.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Ticket id Expo trả về (<c>data[i].id</c>). Đây là khoá tra cứu receipt.
    /// Unique — cùng một ticket id không được đối soát hai lần.
    /// </summary>
    public string TicketId { get; set; } = string.Empty;

    /// <summary>Expo push token đã gửi tới — cần để deactivate đúng thiết bị khi <c>DeviceNotRegistered</c>.</summary>
    public string DeviceToken { get; set; } = string.Empty;

    /// <summary>Trạng thái đối soát.</summary>
    public PushReceiptStatusEnum Status { get; set; } = PushReceiptStatusEnum.Pending;

    /// <summary>Mã lỗi Expo trả về (<c>DeviceNotRegistered</c>, <c>MessageTooBig</c>, …).</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Thông điệp lỗi đầy đủ, phục vụ điều tra.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Lần đối soát gần nhất (UTC). Null = chưa hỏi Expo lần nào.</summary>
    public DateTime? CheckedAt { get; set; }

    /// <summary>
    /// Số lần đã hỏi Expo mà vẫn chưa có kết quả. Expo chỉ giữ receipt 24h — quá
    /// <c>Notification:ExpoReceipt:MaxCheckAttempts</c> lần thì bỏ cuộc để không poll vĩnh viễn.
    /// </summary>
    public int CheckAttemptCount { get; set; }
}

/// <summary>Sprint 6.3 NOTI3-02 (#702) — kết quả đối soát receipt.</summary>
public enum PushReceiptStatusEnum
{
    /// <summary>Đã gửi lên Expo, chưa hỏi được kết quả.</summary>
    Pending = 1,

    /// <summary>Expo xác nhận đã đẩy tới FCM/APNs thành công.</summary>
    Ok = 2,

    /// <summary>Expo báo lỗi — xem <see cref="PushReceipt.ErrorCode"/>.</summary>
    Error = 3,

    /// <summary>Hết hạn cửa sổ 24h của Expo mà chưa có kết quả — không bao giờ biết được nữa.</summary>
    Expired = 4
}
