using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

public class BatteryTypeDto
{
    /// <summary>Định danh resource.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Tên hiển thị.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Tên nhà sản xuất.</summary>
    public string? Manufacturer { get; set; }

    /// <summary>Dung lượng danh nghĩa (Ah).</summary>
    public decimal NominalCapacityAh { get; set; }

    /// <summary>Điện áp danh nghĩa (V).</summary>
    public decimal NominalVoltage { get; set; }

    /// <summary>Hoá học pin (LiFePO4 | NMC | NCA).</summary>
    public BatteryChemistryEnum Chemistry { get; set; }

    /// <summary>Tối đa số chu kỳ trước EOL.</summary>
    public int MaxCycleCount { get; set; }

    /// <summary>Mô tả chi tiết.</summary>
    public string? Description { get; set; }

    /// <summary>Timestamp tạo (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}
