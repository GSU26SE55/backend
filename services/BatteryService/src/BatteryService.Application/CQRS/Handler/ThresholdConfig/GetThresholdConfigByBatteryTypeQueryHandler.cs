using BatteryService.Application.CQRS.Query.ThresholdConfig;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.ThresholdConfig;

public class GetThresholdConfigByBatteryTypeQueryHandler : IRequestHandler<GetThresholdConfigByBatteryTypeQuery, CommonResponse<ThresholdConfigDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetThresholdConfigByBatteryTypeQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<ThresholdConfigDto>> Handle(GetThresholdConfigByBatteryTypeQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.ThresholdConfigs
            .GetAllAsync()
            .AsNoTracking()
            .Include(config => config.BatteryType)
            .Where(config => !config.IsDeleted && config.BatteryTypeId == request.BatteryTypeId);

        if (!request.IncludeInactive)
            query = query.Where(config => config.IsActive);

        var entity = await query
            .OrderByDescending(config => config.EffectiveFromUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            return new CommonResponse<ThresholdConfigDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy cấu hình ngưỡng cho loại pin."
            };
        }

        return new CommonResponse<ThresholdConfigDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = BatteryMapper.ToDto(entity)
        };
    }
}
