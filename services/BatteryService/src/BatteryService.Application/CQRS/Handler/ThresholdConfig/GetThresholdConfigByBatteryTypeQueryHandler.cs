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

        // Chua cau hinh nguong KHONG phai loi: day la mot query thanh cong tra ve tap rong.
        // Tra 404 khien client coi la error -> React Query retry + backoff, va modal
        // "Configure thresholds" (case pho bien nhat: cau hinh lan dau) phai cho vo ich
        // truoc khi hien form trong. Tra 200 voi Data = null de client phan biet
        // "chua cau hinh" (null) voi loi that su (403/500).
        if (entity is null)
        {
            return new CommonResponse<ThresholdConfigDto>
            {
                IsSuccess = true,
                StatusCode = 200,
                Data = null,
                Message = "Threshold configuration not found for this battery type."
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
