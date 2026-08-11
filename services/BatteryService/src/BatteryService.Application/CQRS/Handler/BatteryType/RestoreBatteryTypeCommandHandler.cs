using BatteryService.Application.CQRS.Command.BatteryType;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.BatteryType;

public class RestoreBatteryTypeCommandHandler : IRequestHandler<RestoreBatteryTypeCommand, CommonResponse<object>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public RestoreBatteryTypeCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<object>> Handle(RestoreBatteryTypeCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.BatteryTypes
            .GetAllAsync()
            .FirstOrDefaultAsync(type => type.Id == request.Id && type.IsDeleted, cancellationToken);

        if (entity is null)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Deleted battery type not found."
            };
        }

        var duplicate = await _unitOfWork.BatteryTypes
            .GetAllAsync()
            .AnyAsync(type =>
                type.Id != request.Id &&
                !type.IsDeleted &&
                type.Name.ToLower() == entity.Name.ToLower(), cancellationToken);

        if (duplicate)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Cannot restore because the battery type name is already in use."
            };
        }

        entity.IsDeleted = false;
        entity.DeletedAt = null;
        _unitOfWork.BatteryTypes.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<object>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Battery type restored successfully."
        };
    }
}
