using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Alert;

public class GetAlertsQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<AlertDto>>>
{
    public Guid? BatteryAssetId { get; set; }

    public AlertSeverityEnum? Severity { get; set; }

    public AlertStatusEnum? Status { get; set; }

    /// <summary>Loại trừ alert có status = Merged. Mặc định true — FE chỉ thấy alert gốc.</summary>
    public bool ExcludeMerged { get; set; } = true;

    public DateTime? From { get; set; }

    public DateTime? To { get; set; }
}
