using BatteryService.Application.CQRS.Command.BatteryType;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using BatteryTypeEntity = BatteryService.Domain.Entities.BatteryType;

namespace BatteryService.Application.CQRS.Handler.BatteryType;

public class CreateBatteryTypeCommandHandler : IRequestHandler<CreateBatteryTypeCommand, CommonResponse<BatteryTypeDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public CreateBatteryTypeCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<BatteryTypeDto>> Handle(CreateBatteryTypeCommand request, CancellationToken cancellationToken)
    {
        var normalizedName = request.Name.Trim().ToLower();
        var existed = await _unitOfWork.BatteryTypes
            .GetAllAsync()
            .AnyAsync(type => !type.IsDeleted && type.Name.ToLower() == normalizedName, cancellationToken);

        if (existed)
        {
            return new CommonResponse<BatteryTypeDto>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Tên loại pin đã tồn tại.",
            };
        }

        var entity = new BatteryTypeEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Manufacturer = request.Manufacturer?.Trim(),
            NominalCapacityAh = request.NominalCapacityAh,
            NominalVoltage = request.NominalVoltage,
            Chemistry = request.Chemistry,
            MaxCycleCount = request.MaxCycleCount,
            Description = request.Description?.Trim()
        };

        await _unitOfWork.BatteryTypes.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<BatteryTypeDto>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Tạo loại pin thành công.",
            Data = BatteryMapper.ToDto(entity)
        };
    }
}
