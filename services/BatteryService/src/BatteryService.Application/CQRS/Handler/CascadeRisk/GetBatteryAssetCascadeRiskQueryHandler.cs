using BatteryService.Application.CQRS.Query.CascadeRisk;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.CascadeRisk;

public class GetBatteryAssetCascadeRiskQueryHandler
    : IRequestHandler<GetBatteryAssetCascadeRiskQuery, CommonResponse<CascadeRiskDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetBatteryAssetCascadeRiskQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<CascadeRiskDto>> Handle(
        GetBatteryAssetCascadeRiskQuery request, CancellationToken cancellationToken)
    {
        var asset = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.Id && !a.IsDeleted, cancellationToken);

        if (asset is null)
        {
            return new CommonResponse<CascadeRiskDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy pin."
            };
        }

        return new CommonResponse<CascadeRiskDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new CascadeRiskDto
            {
                BatteryAssetId = asset.Id.ToString(),
                SerialNumber = asset.SerialNumber,
                SiteId = asset.SiteId?.ToString(),
                CascadeRiskScore = asset.CascadeRiskScore,
                Level = CascadeRiskDto.ToLevel(asset.CascadeRiskScore),
                ElectricalTopology = asset.ElectricalTopology,
                CascadeRiskUpdatedAt = asset.CascadeRiskUpdatedAt
            }
        };
    }
}
