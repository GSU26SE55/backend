using AiModule.V1;
using BatteryService.Application.Common.Models;
using BatteryService.Application.Interfaces;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BatteryService.Infrastructure.Implements.Ai;

/// <summary>
/// SOH chuỗi dài qua gRPC <c>PredictLong</c>. Không có HTTP fallback vì đường này KHÔNG
/// nằm trên hot-path — thất bại thì trả <c>null</c> và caller báo 503, không cần cứu.
/// </summary>
public class AiPredictionLongGrpcClient : IAiPredictionLongClient
{
    private readonly AiService.AiServiceClient _client;
    private readonly AiOptions _options;
    private readonly ILogger<AiPredictionLongGrpcClient> _logger;

    public AiPredictionLongGrpcClient(
        AiService.AiServiceClient client,
        IOptions<AiOptions> options,
        ILogger<AiPredictionLongGrpcClient> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiLongPredictionResult?> PredictLongAsync(
        string batteryId,
        IReadOnlyList<double[]> readings,
        AiPackConfig? packConfig = null,
        CancellationToken cancellationToken = default)
    {
        var request = new PredictLongRequest { BatteryId = batteryId };
        foreach (var row in readings)
        {
            var reading = new Reading();
            reading.Values.AddRange(row);
            request.Readings.Add(reading);
        }
        if (packConfig is not null)
        {
            request.PackConfig = new PackConfig { NSeries = packConfig.NSeries };
            if (packConfig.Chemistry is not null)
                request.PackConfig.Chemistry = packConfig.Chemistry;
            if (packConfig.CapacityAh is not null)
                request.PackConfig.CapacityAh = packConfig.CapacityAh.Value;
        }

        try
        {
            // Chuỗi dài tốn nhiều thời gian hơn hẳn window=30 (tới 4096 timestep), nên
            // deadline phải rộng hơn timeout của Predict — dùng chung sẽ cắt ngang một
            // request đang chạy bình thường và trông y như AI hỏng.
            var deadline = DateTime.UtcNow.AddSeconds(Math.Max(60, _options.TimeoutSeconds * 12));
            var resp = await _client.PredictLongAsync(
                request, deadline: deadline, cancellationToken: cancellationToken);

            return new AiLongPredictionResult(
                SohPercent: (decimal)resp.SohPercent,
                SeqLen: resp.SeqLen,
                Device: resp.Device,
                LatencyMs: (int)Math.Round(resp.InferenceMs),
                ModelVersion: resp.ModelVersion);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            _logger.LogWarning(
                "AI từ chối payload PredictLong cho {BatteryId}: {Detail}", batteryId, ex.Status.Detail);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PredictLong lỗi cho {BatteryId}.", batteryId);
            return null;
        }
    }
}
