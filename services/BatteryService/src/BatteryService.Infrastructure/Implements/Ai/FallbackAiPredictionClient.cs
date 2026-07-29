using BatteryService.Application.Common.Models;
using BatteryService.Application.Interfaces;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace BatteryService.Infrastructure.Implements.Ai;

/// <summary>
/// BE-AI — composite Predict client: gRPC PRIMARY → HTTP FALLBACK → null.
///
/// Quy tắc fallback:
///   - Lỗi TRANSPORT (Unavailable/DeadlineExceeded/Internal) → thử HTTP. 2 container AI
///     riêng biệt nên gRPC chết thật thì HTTP (:8000) còn sống.
///   - InvalidArgument (input sai) → KHÔNG fallback, trả null. HTTP dùng chung Pydantic
///     schema nên reject y hệt — fallback chỉ tốn thời gian.
///   - HTTP cũng fail → null. Caller (job) no-op, threshold rule vẫn chạy.
/// </summary>
public class FallbackAiPredictionClient : IAiPredictionClient
{
    private readonly AiPredictionGrpcClient _grpc;
    private readonly AiPredictionHttpClient _http;
    private readonly AiOptions _options;
    private readonly ILogger<FallbackAiPredictionClient> _logger;

    public FallbackAiPredictionClient(
        AiPredictionGrpcClient grpc,
        AiPredictionHttpClient http,
        IOptions<AiOptions> options,
        ILogger<FallbackAiPredictionClient> logger)
    {
        _grpc = grpc;
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiPredictionResult?> PredictAsync(
        string batteryId,
        IReadOnlyList<double[]> readings,
        AiPackConfig? packConfig = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _grpc.PredictAsync(
                batteryId, readings, packConfig, _options.TimeoutSeconds, cancellationToken);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            // Input BE gửi sai (window ≠ 30, feature count, out-of-range) — HTTP cũng reject.
            _logger.LogWarning("AI rejected input for {BatteryId}: {Detail}", batteryId, ex.Status.Detail);
            return null;
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(
                "gRPC AI unavailable ({Code}) for {BatteryId} — falling back to HTTP", ex.StatusCode, batteryId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "gRPC AI call failed for {BatteryId} — falling back to HTTP", batteryId);
        }

        try
        {
            return await _http.PredictAsync(batteryId, readings, packConfig, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP AI fallback also failed for {BatteryId} — no prediction", batteryId);
            return null;
        }
    }
}
