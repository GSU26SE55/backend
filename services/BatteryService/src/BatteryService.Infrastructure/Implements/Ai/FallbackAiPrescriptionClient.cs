using BatteryService.Application.Common.Models;
using BatteryService.Application.Interfaces;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BatteryService.Infrastructure.Implements.Ai;

/// <summary>
/// BE-AI — composite Prescribe client: gRPC PRIMARY → HTTP FALLBACK → null.
/// Same fallback policy as <see cref="FallbackAiPredictionClient"/>.
/// </summary>
public class FallbackAiPrescriptionClient : IAiPrescriptionClient
{
    private readonly AiPrescriptionGrpcClient _grpc;
    private readonly AiPrescriptionHttpClient _http;
    private readonly AiOptions _options;
    private readonly ILogger<FallbackAiPrescriptionClient> _logger;

    public FallbackAiPrescriptionClient(
        AiPrescriptionGrpcClient grpc,
        AiPrescriptionHttpClient http,
        IOptions<AiOptions> options,
        ILogger<FallbackAiPrescriptionClient> logger)
    {
        _grpc = grpc;
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiPrescriptionResult?> PrescribeAsync(
        string batteryId,
        IReadOnlyList<double[]> readings,
        bool enrich = true,
        AiPackConfig? packConfig = null,
        CancellationToken cancellationToken = default,
        AiPrescriptionContext? context = null,
        bool agentic = false)
    {
        try
        {
            // enrich=true chạy RAG + LLM nên mất vài giây — deadline 5s của Predict là quá ngắn.
            // Bản HTTP đã nới lên Math.Max(30, TimeoutSeconds) từ lâu (xem ManageDependencyInjection)
            // nhưng đường gRPC thì chưa, nên MỌI prescribe enrich=true đều DeadlineExceeded sau 5s
            // rồi mới fallback sang HTTP: tốn thêm 5 giây mỗi lần, và log trông như AI đang hỏng
            // trong khi nó chỉ đang làm việc bình thường.
            //
            // Giữ 5s cho enrich=false: đường rule-based chạy <100ms, deadline rộng ở đó chỉ làm
            // chậm việc phát hiện AI thật sự treo.
            var prescribeTimeout = enrich
                ? Math.Max(30, _options.TimeoutSeconds)
                : _options.TimeoutSeconds;

            return await _grpc.PrescribeAsync(
                batteryId, readings, enrich, packConfig, prescribeTimeout, cancellationToken,
                context, agentic);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            _logger.LogWarning("AI rejected prescribe input for {BatteryId}: {Detail}", batteryId, ex.Status.Detail);
            return null;
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(
                "gRPC AI prescribe unavailable ({Code}) for {BatteryId} — falling back to HTTP", ex.StatusCode, batteryId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "gRPC AI prescribe failed for {BatteryId} — falling back to HTTP", batteryId);
        }

        try
        {
            return await _http.PrescribeAsync(
                batteryId, readings, enrich, packConfig, cancellationToken, context, agentic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP AI prescribe fallback also failed for {BatteryId}", batteryId);
            return null;
        }
    }
}
