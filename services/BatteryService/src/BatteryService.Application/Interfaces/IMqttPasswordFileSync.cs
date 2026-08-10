namespace BatteryService.Application.Interfaces;

/// <summary>
/// IOT3-29 — đẩy thông tin đăng nhập MQTT của thiết bị từ DB xuống file <c>passwd</c> của broker
/// NGAY, không đợi hết chu kỳ rà soát nền.
/// </summary>
/// <remarks>
/// <para>
/// <c>MqttPasswordFileSyncService</c> (Infrastructure) quét lại mỗi
/// <c>Mqtt:CredentialSyncIntervalSeconds</c> giây — mặc định 60. Với luồng cấp/xoay khoá thì chờ
/// tới một phút là quá lâu: thiết bị nhận mật khẩu qua <c>/provision</c> rồi nối broker ngay lập
/// tức và bị từ chối <c>state=4 BAD_CREDENTIALS</c>, dù mọi tầng phía trên đều báo thành công.
/// </para>
/// <para>
/// Interface đặt ở Application vì handler không được tham chiếu ngược xuống Infrastructure
/// (be.md §1: <c>Application → Domain only</c>). Cùng khuôn với <c>IMqttBridgePublisher</c>.
/// </para>
/// <para>
/// <b>Lời gọi PHẢI được bọc try-catch.</b> Đồng bộ hỏng (đĩa đầy, mount read-only, broker chưa
/// lên) không được làm <c>/provision</c> thất bại — thiết bị vẫn dùng được đường HTTPS, và vòng
/// quét nền sẽ thử lại. Ném lỗi ra ngoài là biến một sự cố hạ tầng thành thiết bị không boot được.
/// </para>
/// </remarks>
public interface IMqttPasswordFileSync
{
    /// <summary>
    /// Chạy đúng MỘT lượt đồng bộ: đọc thông tin đăng nhập thiết bị từ DB, ghi lại vùng có mốc
    /// trong file <c>passwd</c>. Không ghi nếu nội dung không đổi (tránh bắt broker nạp lại vô cớ).
    /// </summary>
    Task SyncOnceAsync(CancellationToken ct);
}
