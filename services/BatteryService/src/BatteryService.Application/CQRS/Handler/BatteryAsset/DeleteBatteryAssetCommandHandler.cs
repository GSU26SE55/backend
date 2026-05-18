using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.BatteryAsset;

public class DeleteBatteryAssetCommandHandler : IRequestHandler<DeleteBatteryAssetCommand, CommonResponse<object>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public DeleteBatteryAssetCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<object>> Handle(DeleteBatteryAssetCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .FirstOrDefaultAsync(asset => asset.Id == request.Id && !asset.IsDeleted, cancellationToken);

        if (entity is null)
            return NotFound();

        if (entity.BatteryGroupId.HasValue)
        {
            var group = await _unitOfWork.BatteryGroups
                .GetAllAsync()
                .FirstOrDefaultAsync(item => item.Id == entity.BatteryGroupId.Value && !item.IsDeleted, cancellationToken);

            if (group is not null)
            {
                group.BatteryCount = Math.Max(0, group.BatteryCount - 1);
                _unitOfWork.BatteryGroups.UpdateAsync(group);
            }
        }

        _unitOfWork.BatteryAssets.DeleteAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<object>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Xóa tài sản pin thành công."
        };
    }

    private static CommonResponse<object> NotFound()
    {
        return new CommonResponse<object>
        {
            IsSuccess = false,
            StatusCode = 404,
            Message = "Không tìm thấy tài sản pin."
        };
    }
}
