using BatteryService.Application.Ai;
using BatteryService.Application.Common.Models;
using BatteryService.Application.CQRS.Query.SohPrediction;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.SohPrediction;

/// <summary>Dự đoán hàng loạt qua bidi stream (C10).</summary>
public class GetBatchPredictionQueryHandler
    : IRequestHandler<GetBatchPredictionQuery, CommonResponse<BatchPredictionDto>>
{
    private const int MaxAssetsPerCall = 50;

    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IAiPredictionStreamClient _streamClient;
    private readonly IAiHealthClient _healthClient;

    public GetBatchPredictionQueryHandler(
        IBatteryUnitOfWork unitOfWork,
        IAiPredictionStreamClient streamClient,
        IAiHealthClient healthClient)
    {
        _unitOfWork = unitOfWork;
        _streamClient = streamClient;
        _healthClient = healthClient;
    }

    public async Task<CommonResponse<BatchPredictionDto>> Handle(
        GetBatchPredictionQuery request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, MaxAssetsPerCall);

        var assets = await _unitOfWork.BatteryAssets.GetAllAsync()
            .AsNoTracking()
            .Where(a => !a.IsDeleted && a.Status == BatteryStatusEnum.Active)
            .Select(a => new
            {
                a.Id,
                NominalVoltage = a.BatteryType!.NominalVoltage,
                NominalCapacityAh = a.BatteryType.NominalCapacityAh,
                Chemistry = a.BatteryType.Chemistry,
            })
            .Take(limit)
            .ToListAsync(cancellationToken);

        if (assets.Count == 0)
            return Fail(409, "No active battery to predict.");

        // soc_mode đọc từ AI, không suy từ chemistry — gửi sai định nghĩa soc_percent không
        // bị từ chối, nó chỉ lặng lẽ dịch SOH đi.
        var health = await _healthClient.GetHealthAsync(cancellationToken);

        var items = new List<AiPredictionBatchItem>();
        var order = new List<Guid>();

        foreach (var asset in assets)
        {
            var packConfig = BuildPackConfig(asset.NominalVoltage, asset.NominalCapacityAh, asset.Chemistry);
            var socMode = health?.SocModeFor(packConfig.Chemistry);
            var allowDerived = socMode switch
            {
                "cycle" => true,
                "window" or "unknown" => false,
                _ => string.Equals(packConfig.Chemistry, "LFP", StringComparison.OrdinalIgnoreCase),
            };

            var desc = await _unitOfWork.SensorReadings.GetAllAsync()
                .AsNoTracking()
                .Where(r => r.BatteryAssetId == asset.Id
                            && (r.SensorSourceCode == null
                                || r.SensorSourceCode == ""
                                || r.SensorSourceCode == "primary"))
                .OrderByDescending(r => r.Time)
                .Take(AiOptions.WindowSize * 2)
                .ToListAsync(cancellationToken);

            if (desc.Count < AiOptions.WindowSize)
                continue;   // pin chưa đủ lịch sử — bỏ qua, KHÔNG gửi để khỏi làm đứt stream

            var scanned = desc.OrderBy(r => r.Time).ToList();
            var checkRows = scanned
                .Select(r => allowDerived && r.CycleCount.HasValue
                    ? new[] { (double)r.Voltage, (double)r.Current, (double)r.Temperature,
                              0d, r.CycleCount.Value, (double)r.SocPercent }
                    : new[] { (double)r.Voltage, (double)r.Current, (double)r.Temperature })
                .ToList();

            var filtered = AiReadingWindowFilter.Filter(checkRows, packConfig);
            if (filtered.AcceptedCount < AiOptions.WindowSize)
                continue;

            var window = filtered.AcceptedIndices
                .Skip(filtered.AcceptedCount - AiOptions.WindowSize)
                .Select(i => scanned[i])
                .ToList();

            // Bộ soc_mode="cycle" từ chối thẳng payload 4 cột — gửi đi là cầm chắc làm ĐỨT
            // cả stream, kéo theo mọi pin phía sau cũng không được chấm.
            if (allowDerived && !window.All(r => r.CycleCount.HasValue))
                continue;

            var t0 = window[0].Time;
            var readings = window.Select(r =>
            {
                var s = (r.Time - t0).TotalSeconds;
                return allowDerived && r.CycleCount.HasValue
                    ? new double[] { (double)r.Voltage, (double)r.Current, (double)r.Temperature,
                                     s, r.CycleCount.Value, (double)r.SocPercent }
                    : new double[] { (double)r.Voltage, (double)r.Current, (double)r.Temperature, s };
            }).ToList();

            items.Add(new AiPredictionBatchItem(asset.Id.ToString(), readings, packConfig));
            order.Add(asset.Id);
        }

        if (items.Count == 0)
            return Fail(409, "No battery has enough valid readings for batch prediction.");

        var result = await _streamClient.PredictManyAsync(items, cancellationToken);

        // Kết quả về ĐÚNG THỨ TỰ đã gửi (hợp đồng PredictStream), nên ghép theo chỉ số.
        var dtoItems = result.Predictions.Select((p, i) => new BatchPredictionItemDto
        {
            BatteryAssetId = i < order.Count ? order[i].ToString() : string.Empty,
            SohPercent = p.SohPercent,
            Classification = p.Classification.ToString(),
            HealthStage = p.HealthStage,
            RiskLevel = p.RiskLevel,
            ActionCode = p.ActionCode,
            IsBorderline = p.IsBorderline,
            IsTemperatureOod = p.IsTemperatureOod,
        }).ToList();

        return new CommonResponse<BatchPredictionDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new BatchPredictionDto
            {
                Items = dtoItems,
                RequestedCount = result.RequestedCount,
                IsComplete = result.IsComplete,
                AbortReason = result.AbortReason,
            },
        };
    }

    private static AiPackConfig BuildPackConfig(
        decimal nominalVoltage, decimal nominalCapacityAh, BatteryChemistryEnum chemistry)
    {
        var (cellNominal, aiChemistry) = chemistry switch
        {
            BatteryChemistryEnum.LiFePO4 => (3.2m, "LFP"),
            BatteryChemistryEnum.Nmc => (3.7m, "NMC"),
            BatteryChemistryEnum.Nca => (3.6m, "NMC"),
            BatteryChemistryEnum.Lco => (3.7m, "NMC"),
            _ => (3.7m, (string?)null),
        };
        return new AiPackConfig(
            Math.Max(1, (int)Math.Round(nominalVoltage / cellNominal)),
            aiChemistry,
            (double)nominalCapacityAh);
    }

    private static CommonResponse<BatchPredictionDto> Fail(int code, string msg) =>
        new() { IsSuccess = false, StatusCode = code, Message = msg };
}
