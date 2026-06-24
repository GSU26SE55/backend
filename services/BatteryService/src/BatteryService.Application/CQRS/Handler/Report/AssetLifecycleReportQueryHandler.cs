using BatteryService.Application.CQRS.Query.Report;
using BatteryService.Application.DTOs.Reports;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Report;

public class AssetLifecycleReportQueryHandler
    : IRequestHandler<AssetLifecycleReportQuery, CommonResponse<List<AssetLifecycleRow>>>
{
    private readonly IBatteryUnitOfWork _uow;
    public AssetLifecycleReportQueryHandler(IBatteryUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<List<AssetLifecycleRow>>> Handle(
        AssetLifecycleReportQuery request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var assets = await _uow.BatteryAssets.GetAllAsync().AsNoTracking()
            .Where(a => !a.IsDeleted)
            .Select(a => new { a.Id, a.SerialNumber, a.InstallDate })
            .ToListAsync(ct);

        var alertCounts = (await _uow.Alerts.GetAllAsync().AsNoTracking()
            .Where(a => !a.IsDeleted && a.BatteryAssetId != null)
            .GroupBy(a => a.BatteryAssetId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync(ct)).ToDictionary(x => x.Id, x => x.Count);

        var cycleCounts = (await _uow.SensorReadings.GetAllAsync().AsNoTracking()
            .Where(r => r.CycleCount != null)
            .GroupBy(r => r.BatteryAssetId)
            .Select(g => new { Id = g.Key, Max = g.Max(x => x.CycleCount) })
            .ToListAsync(ct)).ToDictionary(x => x.Id, x => x.Max);

        var rows = assets.Select(a => new AssetLifecycleRow
        {
            AssetId = a.Id.ToString(),
            SerialNumber = a.SerialNumber,
            AgeDays = Math.Max(0, (int)(now - a.InstallDate).TotalDays),
            CycleCount = cycleCounts.TryGetValue(a.Id, out var cc) ? cc : null,
            AlertsTotal = alertCounts.TryGetValue(a.Id, out var ac) ? ac : 0
        }).OrderByDescending(r => r.AlertsTotal).ToList();

        return new CommonResponse<List<AssetLifecycleRow>> { IsSuccess = true, StatusCode = 200, Data = rows };
    }
}
