using BatteryService.Application.CQRS.Query.BatteryAsset;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.BatteryAsset;

public class GetBatteryAssetByIdQueryHandler : IRequestHandler<GetBatteryAssetByIdQuery, CommonResponse<BatteryAssetDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetBatteryAssetByIdQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<BatteryAssetDto>> Handle(GetBatteryAssetByIdQuery request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .AsNoTracking()
            .Include(asset => asset.BatteryType)
            .Include(asset => asset.Site)
            .Include(asset => asset.BatteryGroup)
            .FirstOrDefaultAsync(asset => asset.Id == request.Id && !asset.IsDeleted, cancellationToken);

        if (entity is null)
        {
            return new CommonResponse<BatteryAssetDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy tài sản pin."
            };
        }

        return new CommonResponse<BatteryAssetDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = BatteryMapper.ToDto(entity)
        };
    }
}
