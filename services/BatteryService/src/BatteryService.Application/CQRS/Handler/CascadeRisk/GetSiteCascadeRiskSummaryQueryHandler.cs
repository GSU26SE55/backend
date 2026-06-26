using BatteryService.Application.CQRS.Query.CascadeRisk;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.CascadeRisk;

public class GetSiteCascadeRiskSummaryQueryHandler
    : IRequestHandler<GetSiteCascadeRiskSummaryQuery, CommonResponse<SiteCascadeRiskSummaryDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetSiteCascadeRiskSummaryQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<SiteCascadeRiskSummaryDto>> Handle(
        GetSiteCascadeRiskSummaryQuery request, CancellationToken cancellationToken)
    {
        var siteExists = await _unitOfWork.Sites
            .GetAllAsync()
            .AsNoTracking()
            .AnyAsync(s => s.Id == request.SiteId && !s.IsDeleted, cancellationToken);

        if (!siteExists)
        {
            return new CommonResponse<SiteCascadeRiskSummaryDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy site."
            };
        }

        var assets = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .AsNoTracking()
            .Where(a => !a.IsDeleted && a.SiteId == request.SiteId)
            .Select(a => new CascadeRiskDto
            {
                BatteryAssetId = a.Id.ToString(),
                SerialNumber = a.SerialNumber,
                SiteId = a.SiteId!.Value.ToString(),
                CascadeRiskScore = a.CascadeRiskScore,
                Level = a.CascadeRiskScore >= 0.7m ? CascadeRiskLevel.High
                    : a.CascadeRiskScore >= 0.5m ? CascadeRiskLevel.Medium
                    : CascadeRiskLevel.Low,
                ElectricalTopology = a.ElectricalTopology,
                CascadeRiskUpdatedAt = a.CascadeRiskUpdatedAt
            })
            .ToListAsync(cancellationToken);

        var summary = new SiteCascadeRiskSummaryDto
        {
            SiteId = request.SiteId.ToString(),
            TotalAssets = assets.Count,
            HighRiskCount = assets.Count(a => a.Level == CascadeRiskLevel.High),
            MediumRiskCount = assets.Count(a => a.Level == CascadeRiskLevel.Medium),
            LowRiskCount = assets.Count(a => a.Level == CascadeRiskLevel.Low),
            MaxScore = assets.Count > 0 ? assets.Max(a => a.CascadeRiskScore) : 0m,
            HighRiskAssets = assets
                .Where(a => a.Level == CascadeRiskLevel.High)
                .OrderByDescending(a => a.CascadeRiskScore)
                .ToList()
        };

        return new CommonResponse<SiteCascadeRiskSummaryDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = summary
        };
    }
}
