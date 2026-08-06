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

/// <summary>SOH chuỗi dài cho 1 pin (GH-10).</summary>
public class GetLongSohQueryHandler : IRequestHandler<GetLongSohQuery, CommonResponse<LongSohDto>>
{
    /// <summary>Dải AI chấp nhận cho đường long. Kẹp lại thay vì để AI từ chối.</summary>
    private const int MinSeq = 31;
    private const int MaxSeq = 4096;

    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IAiPredictionLongClient _longClient;

    public GetLongSohQueryHandler(IBatteryUnitOfWork unitOfWork, IAiPredictionLongClient longClient)
    {
        _unitOfWork = unitOfWork;
        _longClient = longClient;
    }

    public async Task<CommonResponse<LongSohDto>> Handle(
        GetLongSohQuery request, CancellationToken cancellationToken)
    {
        var asset = await _unitOfWork.BatteryAssets.GetAllAsync()
            .AsNoTracking()
            .Where(a => a.Id == request.BatteryAssetId && !a.IsDeleted)
            .Select(a => new
            {
                NominalVoltage = a.BatteryType!.NominalVoltage,
                NominalCapacityAh = a.BatteryType.NominalCapacityAh,
                Chemistry = a.BatteryType.Chemistry,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (asset is null)
            return Fail(404, "Không tìm thấy pin.");

        var limit = Math.Clamp(request.Limit, MinSeq, MaxSeq);

        // KHÔNG lọc SourceType: dữ liệu production đến từ IoT gateway (source_type=2).
        // Dùng đúng bộ lọc của SohPredictionBackgroundService, nếu không hai đường đọc cùng
        // một bảng lại thấy hai tập khác nhau — pin chạy gateway sẽ luôn "không đủ dữ liệu".
        var desc = await _unitOfWork.SensorReadings.GetAllAsync()
            .AsNoTracking()
            .Where(r => r.BatteryAssetId == request.BatteryAssetId
                        && (r.SensorSourceCode == null
                            || r.SensorSourceCode == ""
                            || r.SensorSourceCode == "primary"))
            .OrderByDescending(r => r.Time)
            .Take(limit)
            .ToListAsync(cancellationToken);

        if (desc.Count < MinSeq)
            return Fail(409, $"Pin cần ít nhất {MinSeq} số đo cho phân tích chuỗi dài, hiện có {desc.Count}.");

        var window = desc.OrderBy(r => r.Time).ToList();
        var packConfig = BuildPackConfig(asset.NominalVoltage, asset.NominalCapacityAh, asset.Chemistry);

        // Đường long chỉ dùng 4 cột gốc — model tự sinh IC-curve + discharge-progress, nên
        // KHÔNG dính bẫy soc_mode như Predict window=30.
        var t0 = window[0].Time;
        var readings = window
            .Select(r => new double[]
            {
                (double)r.Voltage, (double)r.Current, (double)r.Temperature,
                (r.Time - t0).TotalSeconds,
            })
            .ToList();

        // Lọc số đo bất khả thi trước khi gửi: một outlier là AI từ chối cả chuỗi.
        var filtered = AiReadingWindowFilter.Filter(
            window.Select(r => new[] { (double)r.Voltage, (double)r.Current, (double)r.Temperature }).ToList(),
            packConfig);
        if (filtered.AcceptedCount < MinSeq)
            return Fail(409, $"Chỉ còn {filtered.AcceptedCount} số đo hợp lệ sau khi loại ngoại lai.");
        readings = filtered.AcceptedIndices.Select(i => readings[i]).ToList();
        // time phải bắt đầu từ 0 SAU khi lọc, nếu không cột time không còn liên tục từ gốc.
        var baseTime = readings[0][3];
        foreach (var row in readings) row[3] -= baseTime;

        var result = await _longClient.PredictLongAsync(
            request.BatteryAssetId.ToString(), readings, packConfig, cancellationToken);

        if (result is null)
            return Fail(503, "AI không phản hồi cho phân tích chuỗi dài.");

        return new CommonResponse<LongSohDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new LongSohDto
            {
                BatteryAssetId = request.BatteryAssetId.ToString(),
                SohPercent = result.SohPercent,
                SeqLen = result.SeqLen,
                Device = result.Device,
                LatencyMs = result.LatencyMs,
                ModelVersion = result.ModelVersion,
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

    private static CommonResponse<LongSohDto> Fail(int code, string msg) =>
        new() { IsSuccess = false, StatusCode = code, Message = msg };
}
