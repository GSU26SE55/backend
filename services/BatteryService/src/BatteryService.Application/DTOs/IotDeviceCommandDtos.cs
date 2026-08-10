namespace BatteryService.Application.DTOs;

/// <summary>
/// Sprint IoT-2 #IoT2-25 — payload Admin gửi qua POST /api/admin/iot-devices/{id}/command.
/// </summary>
public class IotDeviceCommandPayloadDto
{
    /// <summary>UUID/idempotency key cho command. Backend tự sinh nếu null.</summary>
    public string? CmdId { get; set; }

    /// <summary>
    /// Loại command. Firmware hiện chỉ hiểu BA loại — nguồn:
    /// <c>iot/firmware-esp32/src/cmd/cmd_logic.cpp</c> hàm <c>classifyType</c>:
    /// <list type="bullet">
    ///   <item><c>set_interval</c> (= <c>set-interval</c>) — params <c>{ pollingSeconds }</c> hoặc
    ///         <c>{ pollingIntervalSeconds }</c>, dải hợp lệ [1, 3600]</item>
    ///   <item><c>trigger_ota</c> (= <c>trigger-ota</c>) — không cần params</item>
    ///   <item><c>request_heartbeat</c> (= <c>request-heartbeat</c>) — không cần params</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// ⚠️ Doc cũ ghi <c>reboot | ota | calibrate | sample-now | set-config</c> — <b>không loại nào
    /// firmware hiểu</b>. Frontend chép nguyên danh sách đó vào dropdown, nên Admin gửi lệnh xong
    /// thấy 202 và toast thành công, còn thiết bị thì ack <c>status: "unknown"</c> rồi không làm gì.
    /// Đo được 08/08/2026; đã sửa ở <c>IOT_COMMAND_TYPES</c> phía frontend.
    ///
    /// Trường này vẫn để KIỂU CHUỖI TỰ DO (không đổi thành enum) để gửi được lệnh mới trước khi
    /// firmware kịp lên bản hỗ trợ. Đánh đổi: backend không thể biết firmware hiểu hay không, nên
    /// nguồn sự thật là <c>classifyType</c> — đừng lấy từ tài liệu.
    /// </remarks>
    public string Type { get; set; } = string.Empty;

    /// <summary>Param JSON tự do — device parse theo <see cref="Type"/>.</summary>
    public object? Params { get; set; }
}

/// <summary>
/// Sprint IoT-2 #IoT2-25 — response 202 sau khi enqueue command vào MQTT broker.
/// Ack thực sự từ device đi qua topic <c>solar/{deviceCode}/cmd/ack</c> (bridge log).
/// </summary>
public class IotDeviceCommandAcceptedDto
{
    /// <summary>ID command (UUID/idempotency key — backend tự sinh nếu null).</summary>
    public string CmdId { get; set; } = string.Empty;
    /// <summary>Mã device duy nhất (vd ESP32-001).</summary>
    public string DeviceCode { get; set; } = string.Empty;
    /// <summary>MQTT topic full path.</summary>
    public string Topic { get; set; } = string.Empty;
}
