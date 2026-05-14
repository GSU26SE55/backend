using BatteryService.Application.CQRS.Command.BatteryGroup;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.BatteryGroup;

public class DeleteBatteryGroupCommandHandler : IRequestHandler<DeleteBatteryGroupCommand, CommonResponse<object>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public DeleteBatteryGroupCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<object>> Handle(DeleteBatteryGroupCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.BatteryGroups
            .GetAllAsync()
            .FirstOrDefaultAsync(group => group.Id == request.Id && !group.IsDeleted, cancellationToken);

        if (entity is null)
            return NotFound("Không tìm thấy nhóm pin.");

        var hasAssets = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .AnyAsync(asset => asset.BatteryGroupId == request.Id && !asset.IsDeleted, cancellationToken);

        if (hasAssets)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Không thể xóa nhóm pin vì vẫn còn tài sản pin."
            };
        }

        _unitOfWork.BatteryGroups.DeleteAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<object>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Xóa nhóm pin thành công."
        };
    }

    private static CommonResponse<object> NotFound(string message)
    {
        return new CommonResponse<object>
        {
            IsSuccess = false,
            StatusCode = 404,
            Message = message
        };
    }
}
