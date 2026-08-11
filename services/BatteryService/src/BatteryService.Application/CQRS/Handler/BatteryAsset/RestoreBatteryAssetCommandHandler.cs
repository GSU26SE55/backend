using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.BatteryAsset;

public class RestoreBatteryAssetCommandHandler : IRequestHandler<RestoreBatteryAssetCommand, CommonResponse<object>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public RestoreBatteryAssetCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<object>> Handle(RestoreBatteryAssetCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .FirstOrDefaultAsync(asset => asset.Id == request.Id && asset.IsDeleted, cancellationToken);

        if (entity is null)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Deleted battery asset not found."
            };
        }

        var duplicate = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .AnyAsync(asset =>
                asset.Id != request.Id &&
                !asset.IsDeleted &&
                asset.SerialNumber == entity.SerialNumber, cancellationToken);

        if (duplicate)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Cannot restore because the battery serial number is already in use."
            };
        }

        if (entity.SiteId.HasValue)
        {
            var siteExists = await _unitOfWork.Sites
                .GetAllAsync()
                .AnyAsync(site => site.Id == entity.SiteId.Value && !site.IsDeleted, cancellationToken);

            if (!siteExists)
            {
                return new CommonResponse<object>
                {
                    IsSuccess = false,
                    StatusCode = 409,
                    Message = "Cannot restore because the asset's site has been deleted."
                };
            }
        }

        entity.IsDeleted = false;
        entity.DeletedAt = null;
        _unitOfWork.BatteryAssets.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<object>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Battery asset restored successfully."
        };
    }
}
