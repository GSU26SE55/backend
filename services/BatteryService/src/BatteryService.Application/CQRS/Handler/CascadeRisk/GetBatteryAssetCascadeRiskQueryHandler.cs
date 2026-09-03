using BatteryService.Application.CQRS.Query.CascadeRisk;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.CascadeRisk;

public class GetBatteryAssetCascadeRiskQueryHandler
    : IRequestHandler<GetBatteryAssetCascadeRiskQuery, CommonResponse<CascadeRiskDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly ICascadeRiskCalculator _calculator;

    public GetBatteryAssetCascadeRiskQueryHandler(IBatteryUnitOfWork unitOfWork, ICascadeRiskCalculator calculator)
    {
        _unitOfWork = unitOfWork;
        _calculator = calculator;
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
                Message = "Battery not found."
            };
        }

        // Điểm vẫn lấy từ DB (đã cache, refresh mỗi 5 phút bởi CascadeRiskBackgroundService) — chỉ
        // riêng lý do breakdown là tính live cho request này, vì không lưu DB (xem CascadeRiskDto.RiskFactors).
        var reasons = await _calculator.ExplainAsync(asset.Id, cancellationToken);

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
                CascadeRiskUpdatedAt = asset.CascadeRiskUpdatedAt,
                RiskFactors = reasons
            }
        };
    }
}
