using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Maintenance;

/// <summary>
/// Lịch sử bảo trì định kỳ của một cục pin, kỳ mới nhất trước.
/// </summary>
public class GetMaintenanceCyclesQuery : IRequest<CommonResponse<List<MaintenanceCycleDto>>>
{
    public Guid BatteryAssetId { get; set; }
}
