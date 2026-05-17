using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.BatteryAsset;

public class TransferBatteryAssetOwnerCommandHandler : IRequestHandler<TransferBatteryAssetOwnerCommand, CommonResponse<object>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public TransferBatteryAssetOwnerCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<object>> Handle(TransferBatteryAssetOwnerCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .Include(asset => asset.BatteryGroup)
            .FirstOrDefaultAsync(asset => asset.Id == request.Id && !asset.IsDeleted, cancellationToken);

        if (entity is null)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy tài sản pin."
            };
        }

        if (entity.CustomerId == request.NewCustomerId)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Tài sản pin đã thuộc khách hàng này."
            };
        }

        var customerExists = await _unitOfWork.CustomerAccounts
            .GetAllAsync()
            .AnyAsync(account =>
                account.Id == request.NewCustomerId &&
                account.IsActive &&
                !account.IsDeleted, cancellationToken);

        if (!customerExists)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy khách hàng mới đang hoạt động.",
                ListErrors = { new Errors { Field = nameof(request.NewCustomerId), Detail = "Khách hàng mới không tồn tại hoặc đã bị khóa." } }
            };
        }

        if (entity.BatteryGroupId.HasValue && entity.BatteryGroup is not null)
        {
            entity.BatteryGroup.BatteryCount = Math.Max(0, entity.BatteryGroup.BatteryCount - 1);
            _unitOfWork.BatteryGroups.UpdateAsync(entity.BatteryGroup);
        }

        entity.CustomerId = request.NewCustomerId;
        entity.SiteId = null;
        entity.Site = null;
        entity.BatteryGroupId = null;
        entity.BatteryGroup = null;
        _unitOfWork.BatteryAssets.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<object>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Chuyển chủ sở hữu tài sản pin thành công."
        };
    }
}
