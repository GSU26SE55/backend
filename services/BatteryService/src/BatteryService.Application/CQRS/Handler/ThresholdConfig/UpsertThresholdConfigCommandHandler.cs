using BatteryService.Application.CQRS.Command.ThresholdConfig;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using ThresholdConfigEntity = BatteryService.Domain.Entities.ThresholdConfig;

namespace BatteryService.Application.CQRS.Handler.ThresholdConfig;

public class UpsertThresholdConfigCommandHandler : IRequestHandler<UpsertThresholdConfigCommand, CommonResponse<ThresholdConfigDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public UpsertThresholdConfigCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<ThresholdConfigDto>> Handle(UpsertThresholdConfigCommand request, CancellationToken cancellationToken)
    {
        var batteryType = await _unitOfWork.BatteryTypes
            .GetAllAsync()
            .FirstOrDefaultAsync(type => type.Id == request.BatteryTypeId && !type.IsDeleted, cancellationToken);

        if (batteryType is null)
        {
            return new CommonResponse<ThresholdConfigDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy loại pin."
            };
        }

        var entity = await _unitOfWork.ThresholdConfigs
            .GetAllAsync()
            .Include(config => config.BatteryType)
            .FirstOrDefaultAsync(config =>
                config.BatteryTypeId == request.BatteryTypeId &&
                config.IsActive &&
                !config.IsDeleted, cancellationToken);

        var effectiveFrom = request.EffectiveFromUtc == default ? DateTime.UtcNow : ToUtc(request.EffectiveFromUtc);

        var isNew = entity is null;
        if (isNew)
        {
            entity = new ThresholdConfigEntity
            {
                Id = Guid.NewGuid(),
                BatteryTypeId = request.BatteryTypeId,
                BatteryType = batteryType,
                IsActive = true
            };

            await _unitOfWork.ThresholdConfigs.AddAsync(entity);
        }

        entity!.VoltageMin = request.VoltageMin;
        entity.VoltageMax = request.VoltageMax;
        entity.TemperatureMax = request.TemperatureMax;
        entity.TemperatureMin = request.TemperatureMin;
        entity.SocWarningThreshold = request.SocWarningThreshold;
        entity.SocCriticalThreshold = request.SocCriticalThreshold;
        entity.CurrentMaxCharge = request.CurrentMaxCharge;
        entity.CurrentMaxDischarge = request.CurrentMaxDischarge;
        entity.EffectiveFromUtc = effectiveFrom;
        entity.IsActive = true;

        if (!isNew)
            _unitOfWork.ThresholdConfigs.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        entity.BatteryType = batteryType;

        return new CommonResponse<ThresholdConfigDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Lưu cấu hình ngưỡng pin thành công.",
            Data = BatteryMapper.ToDto(entity)
        };
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }
}
