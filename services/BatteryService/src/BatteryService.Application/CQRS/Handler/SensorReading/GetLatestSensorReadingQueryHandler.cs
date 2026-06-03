using BatteryService.Application.CQRS.Query.SensorReading;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.SensorReading;

public class GetLatestSensorReadingQueryHandler : IRequestHandler<GetLatestSensorReadingQuery, CommonResponse<SensorReadingDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetLatestSensorReadingQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<SensorReadingDto>> Handle(GetLatestSensorReadingQuery request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.SensorReadings
            .GetAllAsync()
            .AsNoTracking()
            .Where(reading => reading.BatteryAssetId == request.BatteryAssetId)
            .OrderByDescending(reading => reading.Time)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            return new CommonResponse<SensorReadingDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy sensor reading."
            };
        }

        return new CommonResponse<SensorReadingDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = BatteryMapper.ToDto(entity)
        };
    }
}
