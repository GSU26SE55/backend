using BatteryService.Application.CQRS.Command.SensorReading;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SensorReadingEntity = BatteryService.Domain.Entities.SensorReading;

namespace BatteryService.Application.CQRS.Handler.SensorReading;

public class BatchIngestSensorReadingsCommandHandler : IRequestHandler<BatchIngestSensorReadingsCommand, CommonResponse<SensorReadingBatchIngestResult>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public BatchIngestSensorReadingsCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<SensorReadingBatchIngestResult>> Handle(BatchIngestSensorReadingsCommand request, CancellationToken cancellationToken)
    {
        var assetIds = request.Items
            .Select(item => item.BatteryAssetId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var assets = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .Where(asset => assetIds.Contains(asset.Id) && !asset.IsDeleted)
            .ToDictionaryAsync(asset => asset.Id, cancellationToken);

        var inserted = 0;
        foreach (var item in request.Items)
        {
            if (!assets.TryGetValue(item.BatteryAssetId, out var asset))
                continue;

            var readingTime = item.Time.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(item.Time, DateTimeKind.Utc)
                : item.Time.ToUniversalTime();

            await _unitOfWork.SensorReadings.AddAsync(new SensorReadingEntity
            {
                Time = readingTime,
                BatteryAssetId = item.BatteryAssetId,
                Voltage = item.Voltage,
                Current = item.Current,
                Temperature = item.Temperature,
                SocPercent = item.SocPercent,
                CycleCount = item.CycleCount,
                SourceDeviceId = item.SourceDeviceId?.Trim()
            });

            if (!asset.LastSensorReadingAt.HasValue || asset.LastSensorReadingAt.Value < readingTime)
                asset.LastSensorReadingAt = readingTime;

            _unitOfWork.BatteryAssets.UpdateAsync(asset);
            inserted++;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<SensorReadingBatchIngestResult>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Ghi nhận sensor readings thành công.",
            Data = new SensorReadingBatchIngestResult
            {
                TotalReceived = request.Items.Count,
                Inserted = inserted,
                Skipped = request.Items.Count - inserted
            }
        };
    }
}
