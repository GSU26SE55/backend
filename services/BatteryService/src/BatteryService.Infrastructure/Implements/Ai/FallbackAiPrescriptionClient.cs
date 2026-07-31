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
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _grpc.PrescribeAsync(
                batteryId, readings, enrich, packConfig, _options.TimeoutSeconds, cancellationToken);
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
            return await _http.PrescribeAsync(batteryId, readings, enrich, packConfig, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP AI prescribe fallback also failed for {BatteryId}", batteryId);
            return null;
        }
    }
}
