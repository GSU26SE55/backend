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
                Message = "Không tìm thấy tài sản pin đã xóa."
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
                Message = "Không thể khôi phục vì serial pin đã được sử dụng."
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
                    Message = "Không thể khôi phục vì site của tài sản đã bị xóa."
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
            Message = "Khôi phục tài sản pin thành công."
        };
    }
}
