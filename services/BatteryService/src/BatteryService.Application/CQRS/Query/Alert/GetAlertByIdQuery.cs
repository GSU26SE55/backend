using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Alert;

public class GetAlertByIdQuery : IRequest<CommonResponse<AlertDto>>
{
    public Guid Id { get; set; }
}
