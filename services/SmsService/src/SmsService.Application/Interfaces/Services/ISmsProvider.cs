namespace SmsService.Application.Interfaces.Services;

/// <summary>
/// Sprint 6.3 NOTI3-05 (#705) — trừu tượng hoá nhà cung cấp SMS.
///
/// **Vì sao tách interface khi vẫn chỉ có một đường gửi (gateway Android)?**
/// Quyết định 30/07/2026 chọn nhánh B: KHÔNG mua provider thứ hai (ngoài ngân sách đồ án).
/// Hệ quả đã ghi nhận ở R-44 — gateway là **một chiếc điện thoại**: hết pin hoặc mất mạng là cả
/// tầng SMS chết, và fallback push→SMS (NOTI3-05) không cứu được ca đó. Cam kết kèm theo là tách
/// sẵn ranh giới này để khi có ngân sách thì cắm Twilio/Vonage chỉ là thêm một lớp
/// <c>ISmsProvider</c> + đổi đăng ký DI, KHÔNG phải mở lại business logic.
///
/// Hiện thực hiện tại là <c>GatewaySmsProvider</c> — nó **xếp hàng** tin nhắn để thiết bị gateway
/// kéo về, chứ không gửi trực tiếp. Vì vậy <see cref="SendAsync"/> trả về ngay khi đã nhận đơn,
/// không đợi tin nhắn thực sự rời máy.
/// </summary>
public interface ISmsProvider
{
    /// <summary>Tên provider — dùng cho log và metric label (<c>android-gateway</c>, …).</summary>
    string ProviderName { get; }

    /// <summary>
    /// Nhận đơn gửi một tin nhắn.
    /// </summary>
    /// <param name="phoneNumber">Số điện thoại người nhận.</param>
    /// <param name="message">Nội dung.</param>
    /// <param name="sourceService">Service phát sinh (<c>notification</c>, <c>auth</c>…) — phục vụ truy vết.</param>
    /// <param name="correlationId">Id nghiệp vụ để đối chiếu ngược (thường là NotificationId).</param>
    /// <param name="ct">Token huỷ.</param>
    /// <returns><c>true</c> nếu đã nhận đơn thành công.</returns>
    Task<bool> SendAsync(
        string phoneNumber,
        string message,
        string sourceService,
        Guid? correlationId = null,
        CancellationToken ct = default);
}
