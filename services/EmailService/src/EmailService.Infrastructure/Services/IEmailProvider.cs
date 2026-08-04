namespace EmailService.Infrastructure.Services;

/// <summary>
/// Sprint 6.3 NOTI3-05 (#705) — trừu tượng hoá nhà cung cấp email.
///
/// **Vì sao tách interface khi vẫn chỉ có một provider (Mailjet)?**
/// Quyết định 30/07/2026 chọn nhánh B cho NOTI3-05: KHÔNG mua provider thứ hai (ngoài ngân sách đồ án).
/// Hệ quả đã ghi nhận ở R-44 — Mailjet vẫn là single point of failure. Đổi lại, cam kết kèm theo là
/// tách sẵn ranh giới này để khi có ngân sách thì cắm provider thứ hai chỉ là thêm một lớp
/// <c>IEmailProvider</c> + đổi đăng ký DI, KHÔNG phải mở lại business logic của consumer/handler.
///
/// <c>EmailSenderService</c> là hiện thực Mailjet. Mọi caller nên phụ thuộc vào interface này.
/// </summary>
public interface IEmailProvider
{
    /// <summary>Tên provider — dùng cho log và metric label (<c>mailjet</c>, …).</summary>
    string ProviderName { get; }

    /// <summary>
    /// Gửi một email HTML. Địa chỉ nằm trong suppression list sẽ bị bỏ qua **im lặng**
    /// (không ném exception) — xem NOTI3-03.
    /// </summary>
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sprint 6.3 NOTI3-15 (#715) — gửi kèm header tuỳ ý (dùng cho <c>List-Unsubscribe</c>).
    ///
    /// Tách overload thay vì đổi chữ ký cũ để 6 consumer email giao dịch không phải sửa gì —
    /// và để việc "email này có nút hủy" là một quyết định hiện rõ ở chỗ gọi.
    /// </summary>
    Task SendAsync(
        string to,
        string subject,
        string htmlBody,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken = default);
}
