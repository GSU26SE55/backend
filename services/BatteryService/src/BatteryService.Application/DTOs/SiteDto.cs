using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

public class SiteDto
{
    /// <summary>Định danh resource.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Tên hiển thị.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>ID Customer (Guid).</summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>Tên của customer.</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Địa chỉ vật lý.</summary>
    public string? Address { get; set; }

    /// <summary>Vĩ độ (-90..90).</summary>
    public decimal? Latitude { get; set; }

    /// <summary>Kinh độ (-180..180).</summary>
    public decimal? Longitude { get; set; }

    /// <summary>Ngày lắp đặt.</summary>
    public DateTime InstallDate { get; set; }

    /// <summary>Filter theo status enum.</summary>
    public SiteStatusEnum Status { get; set; }

    /// <summary>Tên người liên hệ.</summary>
    public string? ContactPersonName { get; set; }

    /// <summary>Số điện thoại người liên hệ.</summary>
    public string? ContactPersonPhone { get; set; }

    /// <summary>Số lượng.</summary>
    public int BatteryAssetCount { get; set; }

    /// <summary>Số lượng.</summary>
    public int ActiveBatteryAssetCount { get; set; }

    /// <summary>Timestamp tạo (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}
