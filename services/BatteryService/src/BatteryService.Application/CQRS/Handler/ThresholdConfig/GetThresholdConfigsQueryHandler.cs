using BatteryService.Application.CQRS.Query.ThresholdConfig;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;

namespace BatteryService.Application.CQRS.Handler.ThresholdConfig;

public class GetThresholdConfigsQueryHandler : IRequestHandler<GetThresholdConfigsQuery, CommonResponse<PaginationResponse<ThresholdConfigDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public GetThresholdConfigsQueryHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<PaginationResponse<ThresholdConfigDto>>> Handle(GetThresholdConfigsQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.ThresholdConfigs
            .GetAllAsync()
            .AsNoTracking()
            .Include(config => config.BatteryType)
            .Where(config => !config.IsDeleted);

        if (request.BatteryTypeId.HasValue)
            query = query.Where(config => config.BatteryTypeId == request.BatteryTypeId.Value);

        if (request.IsActive.HasValue)
            query = query.Where(config => config.IsActive == request.IsActive.Value);

        var page = await query
            .OrderByDescending(config => config.EffectiveFromUtc)
            .ThenBy(config => config.Id) // tie-breaker cố định — pagination ổn định
            .Select(config => new ThresholdConfigDto
            {
                Id = config.Id.ToString(),
                BatteryTypeId = config.BatteryTypeId.ToString(),
                BatteryTypeName = config.BatteryType.Name,
                VoltageMin = config.VoltageMin,
                VoltageMax = config.VoltageMax,
                TemperatureMax = config.TemperatureMax,
                TemperatureMin = config.TemperatureMin,
                SocWarningThreshold = config.SocWarningThreshold,
                SocCriticalThreshold = config.SocCriticalThreshold,
                CurrentMaxCharge = config.CurrentMaxCharge,
                CurrentMaxDischarge = config.CurrentMaxDischarge,
                SohWarningThreshold = config.SohWarningThreshold,
                SohCriticalThreshold = config.SohCriticalThreshold,
                EffectiveFromUtc = config.EffectiveFromUtc,
                IsActive = config.IsActive
            })
            .ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);

        return new CommonResponse<PaginationResponse<ThresholdConfigDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page
        };
    }
}
