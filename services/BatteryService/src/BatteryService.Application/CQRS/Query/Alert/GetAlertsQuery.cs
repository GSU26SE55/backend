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

    public DateTime? From { get; set; }

    public DateTime? To { get; set; }
}
