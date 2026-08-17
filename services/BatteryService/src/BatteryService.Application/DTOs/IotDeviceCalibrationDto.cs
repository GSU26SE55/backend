namespace BatteryService.Application.DTOs;

/// <summary>
/// Sprint IoT-2 #IoT2-32 — DTO calibration profile của 1 channel.
/// </summary>
public class IotDeviceCalibrationDto
{
    /// <summary>Định danh resource.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>ID IoT device (Guid).</summary>
    public string IotDeviceId { get; set; } = string.Empty;

    /// <summary>
    /// Mã thiết bị người-đọc-được (vd "ESP32-SIM-001), lấy từ <c>IotDevice.DeviceCode</c>.
    ///
    /// <para>Chỉ được điền ở danh sách "calibration sắp hết hạn" — bảng đó gộp nhiều thiết bị
    /// nên người xem không có ngữ cảnh nào khác ngoài chính dòng dữ liệu, mà cột "Device ID"
    /// trước đây in thẳng GUID. Các endpoint còn lại luôn nằm trong ngữ cảnh một thiết bị
    /// (đường dẫn đã có id) nên để <c>null</c>, tránh một lượt truy vấn không ai dùng tới.</para>
    /// </summary>
    public string? IotDeviceCode { get; set; }
    /// <summary>Channel sensor (vd "voltage", "current", "temperature").</summary>
    public string Channel { get; set; } = string.Empty;
    /// <summary>ID BatteryAsset (Guid).</summary>
    public string? BatteryAssetId { get; set; }

    /// <summary>
    /// Serial pin người-đọc-được, lấy từ <c>BatteryAsset.SerialNumber</c>.
    ///
    /// <para>Thẻ hiệu chuẩn trên mobile hiện dòng "Battery: …" — trước đây in thẳng GUID.
    /// <c>AlertDto</c> và <c>TicketDTO</c> đều đã kèm serial theo cùng lý do.</para>
    ///
    /// <para><c>null</c> khi calibration ở mức thiết bị (không gắn pin nào) hoặc pin đã bị xoá.</para>
    /// </summary>
    public string? BatterySerialNumber { get; set; }
    /// <summary>Scale factor calibration (default 1.0).</summary>
    public decimal Scale { get; set; }
    /// <summary>Offset cộng thêm calibration (default 0.0).</summary>
    public decimal Offset { get; set; }
    /// <summary>Đơn vị đo (V | A | °C | %).</summary>
    public string Unit { get; set; } = string.Empty;
    /// <summary>Ngày calibration thực hiện.</summary>
    public DateTime CalibratedAt { get; set; }
    /// <summary>Ngày hết hạn.</summary>
    public DateTime? ExpiresAt { get; set; }
    /// <summary>Ghi chú tự do.</summary>
    public string? Notes { get; set; }
    /// <summary>Timestamp tạo (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}
