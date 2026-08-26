using BatteryService.Domain.Enums;
using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

public class BatteryAsset : AuditableEntity
{
    public string SerialNumber { get; set; } = null!;

    public Guid BatteryTypeId { get; set; }

    public Guid? SiteId { get; set; }

    public Guid CustomerId { get; set; }

    public DateTime InstallDate { get; set; }

    public DateTime? WarrantyEndDate { get; set; }

    public WarrantyStatusEnum WarrantyStatus { get; set; } = WarrantyStatusEnum.Active;

    public string? Location { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public BatteryStatusEnum Status { get; set; } = BatteryStatusEnum.Active;

    public string? Notes { get; set; }

    public DateTime? LastSensorReadingAt { get; set; }

    /// <summary>Sprint 7 B4 (§31.7) — điểm rủi ro lan truyền 0.0–1.0, recompute bởi CascadeRiskBackgroundService.</summary>
    public decimal CascadeRiskScore { get; set; }

    /// <summary>Sprint 7 B4 (§31.7) — thời điểm tính CascadeRiskScore lần cuối.</summary>
    public DateTime? CascadeRiskUpdatedAt { get; set; }

    /// <summary>Sprint 7 B4 (§31.7) — cách đấu nối điện, dùng cho topology factor khi tính cascade risk.</summary>
    public ElectricalTopologyEnum ElectricalTopology { get; set; } = ElectricalTopologyEnum.Independent;

    // ── Bảo trì định kỳ ────────────────────────────────────────────────────────
    //
    // Lịch bảo trì là thuộc tính vòng đời của TÀI SẢN, nên nó nằm ở đây. Trước đây
    // chu kỳ được suy ngược mỗi tick từ ticket Closed gần nhất của pin
    // (GroupBy battery_asset_id trên toàn bảng tickets), kéo theo bốn hệ quả:
    // pin chưa từng có ticket Closed thì không bao giờ vào lịch; mọi ticket đóng —
    // kể cả khiếu nại vặt — đều dời chu kỳ thêm một kỳ; chu kỳ cứng cho mọi loại
    // pin; và không thể trả lời "pin nào sắp/đã quá hạn" nếu không quét bảng ticket.
    //
    // NextMaintenanceDueAtUtc là cột thật, có index — nguồn sự thật của lịch.

    /// <summary>Lần bảo trì định kỳ gần nhất đã hoàn tất. <c>null</c> = chưa lần nào.</summary>
    public DateTime? LastMaintenanceAtUtc { get; set; }

    /// <summary>
    /// Kỳ bảo trì kế tiếp. Luôn có giá trị: pin chưa bảo trì lần nào thì tính từ
    /// <see cref="InstallDate"/>.
    /// </summary>
    public DateTime NextMaintenanceDueAtUtc { get; set; }

    /// <summary>Số thứ tự kỳ kế tiếp — 1 là kỳ đầu tiên kể từ khi lắp đặt.</summary>
    public int MaintenanceCycleNo { get; set; } = 1;

    public ICollection<MaintenanceCycle> MaintenanceCycles { get; set; } = new List<MaintenanceCycle>();

    public BatteryType BatteryType { get; set; } = null!;

    public Site? Site { get; set; }

    public ICollection<SensorReading> SensorReadings { get; set; } = new List<SensorReading>();

    public ICollection<Alert> Alerts { get; set; } = new List<Alert>();

    public ICollection<IotDeviceCommand> IotDeviceCommands { get; set; } = new List<IotDeviceCommand>();
}
