using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

public class AlertDto
{
    /// <summary>Định danh resource.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// ID BatteryAsset (Guid). **Chuỗi rỗng `""`** cho alert cấp SITE (ambient NS-21 /
    /// environmental incident) — không gắn pin cụ thể; khi đó dùng <see cref="SiteId"/>.
    /// </summary>
    public string BatteryAssetId { get; set; } = string.Empty;

    /// <summary>IoT device id for a device-level offline incident; null for other alerts.</summary>
    public string? IotDeviceId { get; set; }

    /// <summary>
    /// Sprint Bonus NS-21 (#661) — ID Site cho alert cấp site (ambient 9/10/11, environmental
    /// incident 14). Null cho alert cấp pin thông thường. Giúp FE route alert về đúng site.
    /// </summary>
    public string? SiteId { get; set; }

    /// <summary>Serial của battery liên quan. Rỗng cho alert cấp site.</summary>
    public string BatterySerialNumber { get; set; } = string.Empty;

    /// <summary>
    /// Mã thiết bị IoT (<c>IotDevice.DeviceCode</c>) cho alert cấp thiết bị. Rỗng cho các alert
    /// khác. Màn hình "Device alerts" hiển thị cột này thay cho serial pin — không có nó thì
    /// FE chỉ còn <see cref="IotDeviceId"/> dạng GUID trần, vô nghĩa với người vận hành.
    /// </summary>
    public string IotDeviceCode { get; set; } = string.Empty;

    /// <summary>Tên hiển thị của thiết bị IoT. Rỗng khi alert không gắn thiết bị.</summary>
    public string IotDeviceName { get; set; } = string.Empty;

    /// <summary>
    /// Tên site chứa alert — lấy từ <c>Site.Name</c>. Rỗng khi alert không gắn site. Alert cấp
    /// thiết bị luôn có site (device thuộc về đúng 1 site), nên đây là cột định vị của màn hình
    /// "Device alerts": mất gateway nào, ở site nào.
    /// </summary>
    public string SiteName { get; set; } = string.Empty;

    /// <summary>
    /// Khách hàng sở hữu alert này. Lấy từ BatteryAsset.CustomerId (alert cấp pin) hoặc
    /// Site.CustomerId (alert cấp site) — cùng hai đường mà tenant scope đã dùng để lọc.
    /// Rỗng khi không tra được tài khoản (đã xoá hoặc chưa đồng bộ).
    /// </summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Loại bất thường (xem AnomalyTypeEnum 1..16).</summary>
    public AnomalyTypeEnum AnomalyType { get; set; }

    /// <summary>
    /// Id của EnvironmentalIncident sinh ra alert này (null nếu alert không đến từ sự cố).
    /// FE dùng để mở đúng hộp thoại chi tiết sự cố khi bấm vào dòng.
    /// </summary>
    public string? EnvironmentalIncidentId { get; set; }

    /// <summary>
    /// Loại sự cố môi trường (Smoke | Fire | GasLeak | Flood | Overheat), chỉ có khi alert là bản
    /// sao của một EnvironmentalIncident.
    /// </summary>
    /// <remarks>
    /// Bản sao đó mang <c>AnomalyType = EnvironmentalIncident</c> và KHÔNG có số đo, nên nếu chỉ
    /// dựa vào AnomalyType thì mọi sự cố — rò khí, ngập nước, cháy — đều hiện chung một dòng vô
    /// nghĩa "Environmental incident / 0 incident". Trường này để màn hình hiện đúng "Gas leak".
    /// </remarks>
    public EnvironmentalIncidentTypeEnum? IncidentType { get; set; }

    /// <summary>Severity của alert (Warning | Critical).</summary>
    public AlertSeverityEnum Severity { get; set; }

    /// <summary>Giá trị ngưỡng đã vi phạm.</summary>
    public decimal? ThresholdValue { get; set; }

    /// <summary>Giá trị thực tế đo được.</summary>
    public decimal? ActualValue { get; set; }

    /// <summary>Đơn vị đo (V | A | °C | %).</summary>
    public string? Unit { get; set; }

    /// <summary>Timestamp phát hiện (UTC).</summary>
    public DateTime DetectedAt { get; set; }

    /// <summary>Filter theo status enum.</summary>
    public AlertStatusEnum Status { get; set; }

    /// <summary>ID Ticket liên kết (nullable nếu chưa Saga link).</summary>
    public string? TicketId { get; set; }

    /// <summary>ID user đã acknowledge.</summary>
    public string? AcknowledgedByUserId { get; set; }

    /// <summary>Timestamp acknowledged (UTC).</summary>
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>Timestamp resolved (UTC).</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>Timestamp kết thúc cửa sổ dedup alert.</summary>
    public DateTime DedupWindowEndUtc { get; set; }

    /// <summary>Current AI prescription id, if a prescription has been generated.</summary>
    public string? AiPrescriptionId { get; set; }

    /// <summary>Timestamp tạo (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}
