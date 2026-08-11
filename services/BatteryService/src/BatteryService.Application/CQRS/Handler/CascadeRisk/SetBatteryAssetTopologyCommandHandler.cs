using BatteryService.Application.CQRS.Command.CascadeRisk;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Services;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.CascadeRisk;

public class SetBatteryAssetTopologyCommandHandler
    : IRequestHandler<SetBatteryAssetTopologyCommand, CommonResponse<CascadeRiskDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly ICascadeRiskCalculator _calculator;

    public SetBatteryAssetTopologyCommandHandler(
        IBatteryUnitOfWork unitOfWork, ICascadeRiskCalculator calculator)
    {
        _unitOfWork = unitOfWork;
        _calculator = calculator;
    }

    public async Task<CommonResponse<CascadeRiskDto>> Handle(
        SetBatteryAssetTopologyCommand request, CancellationToken cancellationToken)
    {
        // Field-level validation (Id, ElectricalTopology) đã chạy ở ValidationBehavior pipeline
        // qua IValidatable.ValidateAsync → trả ListErrors. Handler chỉ lo business logic.
        var asset = await _unitOfWork.BatteryAssets.GetByIdAsync(request.Id);
        if (asset is null || asset.IsDeleted)
        {
            return new CommonResponse<CascadeRiskDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Battery not found."
            };
        }

        asset.ElectricalTopology = request.ElectricalTopology;

        // Recompute ngay để response phản ánh topology mới (background service vẫn refresh định kỳ).
        var newScore = await _calculator.CalculateAsync(asset.Id, cancellationToken);
        asset.CascadeRiskScore = newScore;
        asset.CascadeRiskUpdatedAt = DateTime.UtcNow;

        _unitOfWork.BatteryAssets.UpdateAsync(asset);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<CascadeRiskDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Electrical topology updated.",
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
