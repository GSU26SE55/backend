using BatteryService.Application.CQRS.Query.Maintenance;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Maintenance;

public class GetMaintenanceCyclesQueryHandler
    : IRequestHandler<GetMaintenanceCyclesQuery, CommonResponse<List<MaintenanceCycleDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetMaintenanceCyclesQueryHandler(IBatteryUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<List<MaintenanceCycleDto>>> Handle(
        GetMaintenanceCyclesQuery request,
        CancellationToken cancellationToken)
    {
        var cycles = await _unitOfWork.MaintenanceCycles.GetAllAsync()
            .AsNoTracking()
            .Where(cycle => cycle.BatteryAssetId == request.BatteryAssetId && !cycle.IsDeleted)
            .OrderByDescending(cycle => cycle.CycleNo)
            .Select(cycle => new MaintenanceCycleDto
            {
                Id = cycle.Id.ToString(),
                BatteryAssetId = cycle.BatteryAssetId.ToString(),
                CycleNo = cycle.CycleNo,
                DueAtUtc = cycle.DueAtUtc,
                RecordedAtUtc = cycle.RecordedAtUtc,
                SohPercentAtCycle = cycle.SohPercentAtCycle,
                // Nối bất đồng bộ nên có thể còn trống — FE ẩn liên kết khi null.
                TicketId = cycle.TicketId.HasValue ? cycle.TicketId.Value.ToString() : null,
                AvgTemperatureCelsius = cycle.AvgTemperatureCelsius,
                MaxTemperatureCelsius = cycle.MaxTemperatureCelsius,
                MinVoltage = cycle.MinVoltage,
                MaxVoltage = cycle.MaxVoltage,
                CycleCountDelta = cycle.CycleCountDelta,
                AlertCount = cycle.AlertCount,
                CriticalAlertCount = cycle.CriticalAlertCount,
                ReadingCount = cycle.ReadingCount
            })
            .ToListAsync(cancellationToken);

        return new CommonResponse<List<MaintenanceCycleDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = cycles
        };
    }
}
