using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.BatteryAsset;

public class DeleteBatteryAssetCommandHandler : IRequestHandler<DeleteBatteryAssetCommand, CommonResponse<object>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly MediatR.IPublisher _publisher;   // Sprint audit #AUDIT-22

    public DeleteBatteryAssetCommandHandler(IBatteryUnitOfWork unitOfWork, MediatR.IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<CommonResponse<object>> Handle(DeleteBatteryAssetCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .FirstOrDefaultAsync(asset => asset.Id == request.Id && !asset.IsDeleted, cancellationToken);

        if (entity is null)
            return NotFound();

        _unitOfWork.BatteryAssets.DeleteAsync(entity);

        // #AUDIT-22
        await _publisher.Publish(BatteryService.Application.CQRS.Notification.Audit.BatteryAuditTrailNotification.For(
            BatteryService.Domain.Enums.BatteryAuditActionEnum.BatteryDeleted, entity.Id,
            targetDisplay: entity.SerialNumber), cancellationToken);

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
