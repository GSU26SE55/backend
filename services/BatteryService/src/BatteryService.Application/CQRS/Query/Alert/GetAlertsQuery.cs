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

    /// <summary>Severity của alert (Warning | Critical).</summary>
    public AlertSeverityEnum? Severity { get; set; }

    /// <summary>Filter theo status enum.</summary>
    public AlertStatusEnum? Status { get; set; }

    /// <summary>Loại trừ alert có status = Merged. Mặc định true — FE chỉ thấy alert gốc.</summary>
    public bool ExcludeMerged { get; set; } = true;

    /// <summary>Filter theo loại anomaly.</summary>
    public AnomalyTypeEnum? AnomalyType { get; set; }

    /// <summary>
    /// Loại trừ alert mirror của EnvironmentalIncident (<c>AnomalyType = EnvironmentalIncident</c>).
    /// Mỗi incident sinh kèm 1 alert cấp site chỉ để dedup/notification; nó đã có màn hình riêng
    /// (/api/environmental-incidents) nên màn hình "Battery alerts" bật cờ này để không hiện trùng.
    /// Mặc định false — giữ nguyên payload cũ cho các consumer hiện tại.
    /// </summary>
    public bool ExcludeEnvironmentalIncidents { get; set; }

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
