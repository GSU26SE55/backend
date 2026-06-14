namespace BatteryService.Application.DTOs;

/// <summary>
/// Sprint IoT-2 #IoT2-25 — payload Admin gửi qua POST /api/admin/iot-devices/{id}/command.
/// </summary>
public class IotDeviceCommandPayloadDto
{
    /// <summary>UUID/idempotency key cho command. Backend tự sinh nếu null.</summary>
    public string? CmdId { get; set; }

    /// <summary>Loại command: <c>reboot | ota | calibrate | sample-now | set-config | ...</c></summary>
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
