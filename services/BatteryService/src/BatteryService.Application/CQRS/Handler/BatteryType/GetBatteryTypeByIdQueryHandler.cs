using BatteryService.Application.CQRS.Query.BatteryType;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.BatteryType;

public class GetBatteryTypeByIdQueryHandler : IRequestHandler<GetBatteryTypeByIdQuery, CommonResponse<BatteryTypeDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetBatteryTypeByIdQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<BatteryTypeDto>> Handle(GetBatteryTypeByIdQuery request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.BatteryTypes
            .GetAllAsync()
            .AsNoTracking()
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

        return new CommonResponse<BatteryTypeDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = BatteryMapper.ToDto(entity)
        };
    }
}
