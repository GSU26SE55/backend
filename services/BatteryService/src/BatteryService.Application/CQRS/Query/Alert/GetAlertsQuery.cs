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

    /// <summary>Filter timestamp bắt đầu (UTC inclusive).</summary>
    public DateTime? From { get; set; }

    /// <summary>Filter timestamp kết thúc (UTC inclusive).</summary>
    public DateTime? To { get; set; }
}
