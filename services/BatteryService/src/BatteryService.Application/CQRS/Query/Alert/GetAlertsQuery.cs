using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Alert;

public class GetAlertsQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<AlertDto>>>
{
    /// <summary>ID BatteryAsset (Guid).</summary>
    public Guid? BatteryAssetId { get; set; }

    /// <summary>
    /// Lọc theo site — dùng bởi màn hình "Environmental alerts" (alert cấp site không có pin để
    /// lọc qua <see cref="BatteryAssetId"/>).
    /// </summary>
    public Guid? SiteId { get; set; }

    /// <summary>Severity của alert (Warning | Critical).</summary>
    public AlertSeverityEnum? Severity { get; set; }

    /// <summary>Filter theo status enum.</summary>
    public AlertStatusEnum? Status { get; set; }

    /// <summary>Loại trừ alert có status = Merged. Mặc định true — FE chỉ thấy alert gốc.</summary>
    public bool ExcludeMerged { get; set; } = true;

    /// <summary>Filter theo loại anomaly.</summary>
    public AnomalyTypeEnum? AnomalyType { get; set; }

    /// <summary>
    /// Loại trừ MỌI alert cấp site (alert không gắn pin: mirror của EnvironmentalIncident, và
    /// ngưỡng môi trường nhiệt độ/độ ẩm/khí gas). Màn hình "Battery alerts" bật cờ này vì alert
    /// cấp site không có serial pin để hiện. Bị bỏ qua khi <c>AnomalyType</c> được truyền.
    /// Mặc định false — giữ nguyên payload cũ cho các consumer hiện tại.
    /// </summary>
    public bool ExcludeEnvironmentalIncidents { get; set; }

    /// <summary>
    /// Mặt đối của <see cref="ExcludeEnvironmentalIncidents"/>: CHỈ lấy alert cấp site — dùng bởi
    /// bảng "Vượt ngưỡng" trong màn hình "Environmental alerts".
    /// </summary>
    /// <remarks>
    /// Alert cấp thiết bị IoT cũng không gắn pin, nên riêng "không có pin" chưa đủ để tách: thiếu
    /// phần trừ hai loại đó ra thì alert gateway sẽ hiện lẫn trong danh sách môi trường, và cùng
    /// một alert bị đếm ở cả hai badge. Ba danh sách phải rời nhau hoàn toàn.
    /// Bị bỏ qua khi <c>AnomalyType</c> được truyền.
    /// </remarks>
    public bool SiteLevelOnly { get; set; }

    /// <summary>
    /// Chỉ lấy alert cấp thiết bị IoT (<c>DeviceOffline</c>, <c>IotDataIntegrityViolation</c>) —
    /// màn hình "Device alerts" của FE bật cờ này. Hai loại đó gắn <c>IotDeviceId</c> chứ không
    /// gắn pin, nên hiện chung với alert pin thì cột serial rỗng và người đọc không biết sự cố
    /// thuộc về gateway hay về pin. Bị bỏ qua khi <c>AnomalyType</c> được truyền.
    /// Mặc định false — giữ nguyên payload cũ cho các consumer hiện tại.
    /// </summary>
    public bool IotOnly { get; set; }

    /// <summary>
    /// Loại trừ alert cấp thiết bị IoT (<c>DeviceOffline</c>, <c>IotDataIntegrityViolation</c>) —
    /// mặt đối của <see cref="IotOnly"/>, dùng bởi màn hình "Battery alerts" để hai danh sách
    /// không chồng nhau. Bị bỏ qua khi <c>AnomalyType</c> được truyền.
    /// Mặc định false — giữ nguyên payload cũ cho các consumer hiện tại.
    /// </summary>
    public bool ExcludeIotDeviceAlerts { get; set; }

    /// <summary>Filter timestamp bắt đầu (UTC inclusive).</summary>
    public DateTime? From { get; set; }

    /// <summary>Filter timestamp kết thúc (UTC inclusive).</summary>
    public DateTime? To { get; set; }
}
