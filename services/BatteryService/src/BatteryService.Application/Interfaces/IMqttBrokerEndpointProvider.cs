namespace BatteryService.Application.Interfaces;

/// <summary>GH-784 — điểm kết nối broker mà thiết bị dùng được, kèm quy ước topic.</summary>
/// <param name="Host">Host broker. Null khi MQTT chưa bật.</param>
/// <param name="Port">Cổng broker.</param>
/// <param name="UseTls">Broker có yêu cầu TLS không — thiết bị cần biết để cấu hình đúng.</param>
/// <param name="TopicPrefix">
/// Tiền tố topic thiết bị PHẢI dùng, đã chuẩn hoá chữ thường.
/// </param>
public readonly record struct MqttBrokerEndpoint(string? Host, int? Port, bool UseTls, string? TopicPrefix)
{
    public static MqttBrokerEndpoint Disabled => new(null, null, false, null);
}

/// <summary>
/// GH-784 — cấp thông tin kết nối broker cho luồng tạo/xoay khoá thiết bị.
/// </summary>
/// <remarks>
/// <para>
/// DTO đã có sẵn <c>MqttBrokerHost</c>/<c>MqttBrokerPort</c> nhưng KHÔNG nơi nào gán ⇒ luôn null.
/// Thiết bị nhận về username/password mà không biết nối đi đâu, nên credential vừa cấp là vô dụng
/// ngay cả khi broker đã biết nó.
/// </para>
/// <para>
/// Đặt ở Application dưới dạng interface vì <c>MqttOptions</c> nằm ở Infrastructure — handler
/// không được tham chiếu ngược xuống đó.
/// </para>
/// </remarks>
public interface IMqttBrokerEndpointProvider
{
    /// <summary>
    /// Điểm kết nối cho <paramref name="deviceCode"/>. Trả <see cref="MqttBrokerEndpoint.Disabled"/>
    /// khi MQTT tắt — thà nói rõ "chưa bật" còn hơn trả host rỗng để thiết bị thử rồi thất bại.
    /// </summary>
    MqttBrokerEndpoint Resolve(string deviceCode);
}
