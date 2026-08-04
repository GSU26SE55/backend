using System.ComponentModel.DataAnnotations.Schema;
using NotificationService.Domain.Enums;
using SharedKernels.Domain;

namespace NotificationService.Domain.Entities;

/// <summary>
/// Sprint 6.4 NOTI4-06 — <b>một lần gửi</b>: nội dung lưu ĐÚNG MỘT LẦN, dù tới bao nhiêu người.
///
/// <para>Trước sprint này không có khái niệm này. Bảng <c>notifications</c> là 1 dòng / người /
/// kênh với <c>title</c>, <c>body</c>, <c>payload_json</c> <b>chép lại nguyên văn từng dòng</b>, và
/// không có khoá nào gom chúng lại. Hậu quả đo được trên môi trường thật: 1.282 dòng thuộc khoảng
/// 242 lần gửi, nhưng muốn biết "thông báo X đã tới những ai" thì phải gom mò theo
/// <c>(type, entity_id, giây)</c> — cách gom đó sai, vì cùng một <c>entity_id</c> có tới 50 dòng
/// trong một giây.</para>
///
/// <para><b>Nội dung vẫn được chép sang từng dòng <c>Notification</c> ở sprint này.</b> Bỏ hẳn
/// <c>title</c>/<c>body</c> khỏi bảng đó là giai đoạn C, đã hoãn có chủ đích (§17.6.4.5 fork 2):
/// nó bắt <c>GET /api/notifications</c> — truy vấn nóng nhất — phải JOIN thêm một bảng, và ép
/// digest/dispatcher vào mô hình batch mà chúng không tự nhiên thuộc về. Vậy nên bảng này giải
/// quyết bài toán <i>truy vết và gom nhóm</i>, chưa giải quyết bài toán <i>trùng lặp dung lượng</i>.</para>
/// </summary>
public class NotificationBatch : AuditableEntity
{
    public NotificationTypeEnum Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>JSON payload bổ sung — deep link, entity ref, key-value cho client tự render.</summary>
    public string? PayloadJson { get; set; }

    /// <summary>Loại entity nghiệp vụ liên quan (Ticket/Battery/...).</summary>
    public string? EntityType { get; set; }

    public Guid? EntityId { get; set; }

    /// <summary>
    /// Các kênh lần gửi này nhắm tới, lưu dưới dạng <b>số nguyên</b>.
    ///
    /// <para>Cố ý KHÔNG dùng <c>NotificationChannelEnum[]</c> kèm value converter: converter
    /// <c>enum[] → int[]</c> không ghép được với provider InMemory (nó tự có converter
    /// <c>IEnumerable&lt;int&gt; → string</c>), làm vỡ toàn bộ test dùng DbContext trong bộ nhớ với
    /// một thông báo lỗi chẳng liên quan gì tới notification. Mảng số nguyên thì cả Npgsql
    /// (<c>integer[]</c>) lẫn InMemory đều hiểu sẵn.</para>
    ///
    /// <para>Dùng <see cref="Channels"/> để đọc/ghi bằng kiểu enum.</para>
    /// </summary>
    public int[] ChannelValues { get; set; } = Array.Empty<int>();

    /// <summary>
    /// Các kênh lần gửi này nhắm tới. Là <b>ý định gửi</b>, không phải kết quả:
    /// <c>NotificationDispatcher</c> vẫn lọc lại theo tuỳ chọn người nhận và quiet hours.
    ///
    /// <para>Không ánh xạ xuống DB — chỉ là lớp bọc kiểu cho <see cref="ChannelValues"/>.</para>
    /// </summary>
    [NotMapped]
    public NotificationChannelEnum[] Channels
    {
        get => ChannelValues.Select(v => (NotificationChannelEnum)v).ToArray();
        set => ChannelValues = value.Select(c => (int)c).ToArray();
    }

    public NotificationBatchSourceEnum Source { get; set; } = NotificationBatchSourceEnum.Manual;

    /// <summary>Template đã dùng, nếu có. Không đặt khoá ngoại — template xoá đi thì lịch sử vẫn giữ.</summary>
    public Guid? TemplateId { get; set; }

    /// <summary>
    /// 03/08/2026 — lần gửi thủ công này có render qua mẫu hay không.
    ///
    /// <para><b>Vì sao là cờ riêng chứ không suy ra từ <see cref="TemplateId"/>:</b> mẫu được khoá
    /// theo cặp <c>(Loại × Kênh)</c>, nên một lần gửi nhắm 3 kênh sẽ dùng <b>3 mẫu khác nhau</b> —
    /// SMS còn có bản nén ngắn riêng. Không có "một" template id để ghi. Dispatcher vẫn tra mẫu theo
    /// từng kênh như thường; cờ này chỉ trả lời câu hỏi có/không.</para>
    ///
    /// <para><c>false</c> (mặc định) ⇒ nội dung admin gõ là thứ có thẩm quyền, dispatcher **bỏ qua**
    /// mẫu. Đó là hành vi đúng cho thông báo viết tay: trước 03/08/2026 mẫu vẫn đè lên và xoá sạch
    /// chữ admin vừa gõ.</para>
    ///
    /// <para><c>true</c> ⇒ admin cố ý chọn dùng mẫu và đã điền biến vào <see cref="PayloadJson"/>.
    /// Kênh nào không có mẫu khớp thì vẫn rơi về Title/Body — không chặn việc gửi.</para>
    /// </summary>
    public bool UseTemplate { get; set; }

    public NotificationBatchStatusEnum Status { get; set; } = NotificationBatchStatusEnum.Pending;

    /// <summary>Số người nhận SAU KHI gom trùng và loại người không hoạt động.</summary>
    public int RecipientCount { get; set; }

    /// <summary>Số dòng <see cref="Notification"/> thực sự đã sinh ra (= người nhận × số kênh).</summary>
    public int NotificationCount { get; set; }

    /// <summary>Nhóm / cá nhân mà lần gửi này nhắm tới.</summary>
    public ICollection<NotificationBatchTarget> Targets { get; set; } = new List<NotificationBatchTarget>();
}
