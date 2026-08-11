using BatteryService.Application.CQRS.Command.BatteryType;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.BatteryType;

public class UpdateBatteryTypeCommandHandler : IRequestHandler<UpdateBatteryTypeCommand, CommonResponse<BatteryTypeDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public UpdateBatteryTypeCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<BatteryTypeDto>> Handle(UpdateBatteryTypeCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.BatteryTypes
            .GetAllAsync()
            .FirstOrDefaultAsync(type => type.Id == request.Id && !type.IsDeleted, cancellationToken);

        if (entity is null)
        {
            return new CommonResponse<BatteryTypeDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Battery type not found."
            };
        }

        var normalizedName = request.Name.Trim().ToLower();
        var duplicate = await _unitOfWork.BatteryTypes
            .GetAllAsync()
            .AnyAsync(type =>
                type.Id != request.Id &&
                !type.IsDeleted &&
                type.Name.ToLower() == normalizedName, cancellationToken);

        if (duplicate)
        {
            return new CommonResponse<BatteryTypeDto>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Battery type name already exists.",
            };
        }

        entity.Name = request.Name.Trim();
        entity.Manufacturer = request.Manufacturer?.Trim();
        entity.NominalCapacityAh = request.NominalCapacityAh;
        entity.NominalVoltage = request.NominalVoltage;
        entity.Chemistry = request.Chemistry;
        entity.MaxCycleCount = request.MaxCycleCount;
        entity.Description = request.Description?.Trim();

        _unitOfWork.BatteryTypes.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<BatteryTypeDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Battery type updated successfully.",
            Data = BatteryMapper.ToDto(entity)
        };
    }
}
