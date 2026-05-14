using BatteryService.Application.CQRS.Command.BatteryType;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.BatteryType;

public class DeleteBatteryTypeCommandHandler : IRequestHandler<DeleteBatteryTypeCommand, CommonResponse<object>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public DeleteBatteryTypeCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<object>> Handle(DeleteBatteryTypeCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.BatteryTypes
            .GetAllAsync()
            .FirstOrDefaultAsync(type => type.Id == request.Id && !type.IsDeleted, cancellationToken);

        if (entity is null)
            return NotFound();

        var hasAssets = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .AnyAsync(asset => asset.BatteryTypeId == request.Id && !asset.IsDeleted, cancellationToken);

        if (hasAssets)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Không thể xóa loại pin đang được gán cho tài sản pin."
            };
        }

        _unitOfWork.BatteryTypes.DeleteAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<object>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Xóa loại pin thành công."
        };
    }

    private static CommonResponse<object> NotFound()
    {
        return new CommonResponse<object>
        {
            IsSuccess = false,
            StatusCode = 404,
            Message = "Không tìm thấy loại pin."
        };
    }
}
