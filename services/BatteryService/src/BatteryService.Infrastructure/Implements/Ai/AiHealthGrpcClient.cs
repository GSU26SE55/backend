using AiModule.V1;
using BatteryService.Application.Common.Models;

namespace BatteryService.Infrastructure.Implements.Ai;

/// <summary>
/// BE-AI — gRPC impl của Health (PRIMARY). Gọi <c>AiService.Health</c> trên :50051.
/// </summary>
public class AiHealthGrpcClient
{
    private readonly AiService.AiServiceClient _client;

    public AiHealthGrpcClient(AiService.AiServiceClient client) => _client = client;

    // virtual: cho phép Moq override trong unit test FallbackAiHealthClient
    // (gRPC AsyncUnaryCall khó stub trực tiếp) — cùng lý do với AiPredictionGrpcClient.
    public virtual async Task<AiHealthResult> GetHealthAsync(
        int timeoutSeconds, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var resp = await _client.HealthAsync(
            new HealthRequest(), deadline: deadline, cancellationToken: cancellationToken);

        return new AiHealthResult(
            Status: resp.Status,
            ModelVersion: resp.ModelVersion,
            ScalerLoaded: resp.ScalerLoaded,
            MambaLoaded: resp.MambaLoaded,
            IsolationForestLoaded: resp.IsolationForestLoaded,
            LfpLoaded: resp.LfpLoaded,
            LfpModelVersion: resp.LfpModelVersion,
            SocMode: resp.SocMode,
            LfpSocMode: resp.LfpSocMode,
            LongLoaded: resp.LongLoaded,
            LongModelVersion: resp.LongModelVersion);
    }
}
