namespace BatteryService.Application.DTOs;

/// <summary>
/// IOT3-58 — một mẫu heartbeat của IoT device.
/// </summary>
/// <remarks>
/// Đây là nguồn duy nhất trả lời được "thiết bị này có khoẻ không" mà không phải ra hiện trường:
/// sóng WiFi yếu dần, RAM tụt dần, hàng đợi cục bộ phình lên, đồng hồ lệch — cả bốn đều là dấu
/// hiệu sớm, và cả bốn đều nằm trong cùng một bản ghi.
/// </remarks>
public class IotDeviceHeartbeatDto
{
    /// <summary>Thời điểm backend ghi nhận (UTC).</summary>
    public DateTime Time { get; set; }

    /// <summary>Firmware thiết bị tự khai (vd "1.2.3").</summary>
    public string? FirmwareVersion { get; set; }

    /// <summary>Sóng WiFi (dBm). Luôn ÂM: −50 mạnh, −90 gần như không dùng được.</summary>
    public int? RssiDbm { get; set; }

    /// <summary>% RAM còn trống (0–100).</summary>
    public decimal? FreeMemoryPercent { get; set; }

    /// <summary>Thiết bị đã chạy liên tục bao lâu (giây). Tụt về gần 0 nghĩa là vừa khởi động lại.</summary>
    public long? UptimeSeconds { get; set; }

    /// <summary>Số bản ghi còn nằm trong hàng đợi cục bộ vì chưa đẩy lên được.</summary>
    public int? QueuedReadingCount { get; set; }

    /// <summary>Đồng hồ thiết bị tự khai (UTC).</summary>
    public DateTime? DeviceTimestamp { get; set; }

    /// <summary>
    /// Lệch đồng hồ thiết bị so với máy chủ (giây). Vượt ±300 s là backend từ chối provision
    /// (§52.3), nên trường này giải thích được vì sao một thiết bị "im lặng" mà không có lỗi mạng.
    /// </summary>
    public double? ClockSkewSeconds { get; set; }
}

/// <summary>
/// IOT3-58 — trang kết quả heartbeat, phân trang theo CON TRỎ.
/// </summary>
/// <remarks>
/// Không dùng offset: <c>iot_device_heartbeats</c> là hypertable TimescaleDB, mỗi thiết bị sinh
/// một bản ghi mỗi 60 giây ⇒ hàng triệu dòng, và <c>OFFSET</c> buộc quét từ đầu (be.md §13).
/// Cũng vì vậy <see cref="TotalCount"/> luôn <c>null</c> — đếm đủ còn đắt hơn cả truy vấn chính.
/// </remarks>
public class IotDeviceHeartbeatListDto
{
    public List<IotDeviceHeartbeatDto> Items { get; set; } = new();

    /// <summary>Truyền lại làm <c>cursor</c> để lấy trang kế. <c>null</c> khi hết dữ liệu.</summary>
    public DateTime? NextCursor { get; set; }

    public bool HasMore { get; set; }

    /// <summary>LUÔN <c>null</c> cho dữ liệu chuỗi thời gian — xem ghi chú của lớp.</summary>
    public int? TotalCount { get; set; }
}
