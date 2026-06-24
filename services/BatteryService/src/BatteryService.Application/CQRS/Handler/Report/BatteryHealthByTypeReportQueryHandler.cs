using BatteryService.Application.CQRS.Query.Report;
using BatteryService.Application.DTOs.Reports;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Report;

public class BatteryHealthByTypeReportQueryHandler
    : IRequestHandler<BatteryHealthByTypeReportQuery, CommonResponse<List<BatteryHealthByTypeRow>>>
{
    private readonly IBatteryUnitOfWork _uow;
    public BatteryHealthByTypeReportQueryHandler(IBatteryUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<List<BatteryHealthByTypeRow>>> Handle(
        BatteryHealthByTypeReportQuery request, CancellationToken ct)
    {
        var types = await _uow.BatteryTypes.GetAllAsync().AsNoTracking()
            .Where(t => !t.IsDeleted)
            .Select(t => new { t.Id, t.Name })
            .ToListAsync(ct);

        var assets = await _uow.BatteryAssets.GetAllAsync().AsNoTracking()
            .Where(a => !a.IsDeleted)
            .Select(a => new { a.Id, a.BatteryTypeId })
            .ToListAsync(ct);

        var activeAlertAssetIds = (await _uow.Alerts.GetAllAsync().AsNoTracking()
            .Where(a => !a.IsDeleted && a.Status == AlertStatusEnum.Open && a.BatteryAssetId != null)
            .Select(a => a.BatteryAssetId!.Value)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();

        var rows = types.Select(t =>
        {
            var typeAssets = assets.Where(a => a.BatteryTypeId == t.Id).ToList();
            var total = typeAssets.Count;
            var withAlerts = typeAssets.Count(a => activeAlertAssetIds.Contains(a.Id));
            return new BatteryHealthByTypeRow
            {
                TypeId = t.Id.ToString(),
                Name = t.Name,
                TotalAssets = total,
                WithActiveAlerts = withAlerts,
                HealthScore = total == 0 ? 100m : Math.Round((total - withAlerts) * 100m / total, 1)
            };
        }).OrderByDescending(r => r.TotalAssets).ToList();

        return new CommonResponse<List<BatteryHealthByTypeRow>> { IsSuccess = true, StatusCode = 200, Data = rows };
    }
}
