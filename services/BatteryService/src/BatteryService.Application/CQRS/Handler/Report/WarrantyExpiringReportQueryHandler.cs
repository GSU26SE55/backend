using BatteryService.Application.CQRS.Query.Report;
using BatteryService.Application.DTOs.Reports;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Report;

public class WarrantyExpiringReportQueryHandler
    : IRequestHandler<WarrantyExpiringReportQuery, CommonResponse<List<WarrantyExpiringRow>>>
{
    private readonly IBatteryUnitOfWork _uow;
    public WarrantyExpiringReportQueryHandler(IBatteryUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<List<WarrantyExpiringRow>>> Handle(
        WarrantyExpiringReportQuery request, CancellationToken ct)
    {
        var days = ParseWithin(request.Within);
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(days);

        var assets = await _uow.BatteryAssets.GetAllAsync().AsNoTracking()
            .Where(a => !a.IsDeleted
                && a.WarrantyEndDate != null
                && a.WarrantyEndDate >= now
                && a.WarrantyEndDate <= cutoff)
            .Select(a => new { a.Id, a.SerialNumber, a.WarrantyEndDate, a.CustomerId })
            .ToListAsync(ct);

        var rows = assets.Select(a => new WarrantyExpiringRow
        {
            AssetId = a.Id.ToString(),
            SerialNumber = a.SerialNumber,
            WarrantyEndDate = a.WarrantyEndDate,
            DaysRemaining = a.WarrantyEndDate.HasValue ? (int)(a.WarrantyEndDate.Value - now).TotalDays : null,
            CustomerId = a.CustomerId.ToString()
        }).OrderBy(r => r.WarrantyEndDate).ToList();

        return new CommonResponse<List<WarrantyExpiringRow>> { IsSuccess = true, StatusCode = 200, Data = rows };
    }

    private static int ParseWithin(string? within)
    {
        if (string.IsNullOrWhiteSpace(within))
            return 90;
        var digits = new string(within.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var d) && d > 0 ? d : 90;
    }
}
