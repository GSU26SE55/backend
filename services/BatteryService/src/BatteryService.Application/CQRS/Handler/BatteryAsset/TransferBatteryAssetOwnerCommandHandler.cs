using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Handler.BatteryAsset;

public class TransferBatteryAssetOwnerCommandHandler : IRequestHandler<TransferBatteryAssetOwnerCommand, CommonResponse<object>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IIntegrationEventOutboxWriter _outbox;

    public TransferBatteryAssetOwnerCommandHandler(IBatteryUnitOfWork unitOfWork, IIntegrationEventOutboxWriter outbox)
    {
        _unitOfWork = unitOfWork;
        _outbox = outbox;
    }

    public async Task<CommonResponse<object>> Handle(TransferBatteryAssetOwnerCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .FirstOrDefaultAsync(asset => asset.Id == request.Id && !asset.IsDeleted, cancellationToken);

        if (entity is null)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Battery asset not found."
            };
        }

        if (entity.CustomerId == request.NewCustomerId)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Battery asset already belongs to this customer."
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
                Message = "Active new customer not found.",
            };
        }

        var previousCustomerId = entity.CustomerId;
        var previousSiteId = entity.SiteId;

        entity.CustomerId = request.NewCustomerId;
        entity.SiteId = null;
        entity.Site = null;
        _unitOfWork.BatteryAssets.UpdateAsync(entity);

        await _outbox.WriteAsync(new BatteryAssetTransferredEvent(
            BatteryAssetId: entity.Id,
            PreviousCustomerId: previousCustomerId,
            NewCustomerId: request.NewCustomerId,
            PreviousSiteId: previousSiteId,
            NewSiteId: null,
            SerialNumber: entity.SerialNumber,
            TransferredAt: DateTime.UtcNow,
            PerformedByUserId: request.PerformedByUserId), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<object>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Battery asset owner transferred successfully."
        };
    }
}
