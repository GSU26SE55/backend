using AiModule.V1;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// BE-AI — gRPC impl của VerifyTicket. Gọi AiService.VerifyTicket trên :50051.
/// Human-in-the-loop: AI chỉ gắn nhãn thật/rác + dò trùng, Manager quyết.
/// Catch RpcException/Exception → trả null (verify skip, KHÔNG chặn ticket).
/// </summary>
public class AiTicketVerifyGrpcClient : IAiTicketVerifyClient
{
    private readonly AiService.AiServiceClient _client;
    private readonly ILogger<AiTicketVerifyGrpcClient> _logger;
    private readonly int _timeoutSeconds;

    public AiTicketVerifyGrpcClient(
        AiService.AiServiceClient client,
        ILogger<AiTicketVerifyGrpcClient> logger,
        int timeoutSeconds)
    {
        _client = client;
        _logger = logger;
        _timeoutSeconds = timeoutSeconds;
    }

    public async Task<AiVerifyResult?> VerifyAsync(
        string title,
        string description,
        DateTime? detectedAt,
        int category,
        TicketSensorSnapshotDto? sensor,
        IReadOnlyList<DuplicateCandidateDto> candidates,
        CancellationToken ct)
    {
        try
        {
            var request = new VerifyTicketRequest
            {
                Title = title ?? string.Empty,
                Description = description ?? string.Empty,
                DetectedAt = detectedAt?.ToUniversalTime().ToString("o") ?? string.Empty,
                Category = category,
                HasSensorSnapshot = sensor is not null
            };

            if (sensor is not null)
            {
                request.SensorSnapshot = new TicketSensorSnapshot
                {
                    SohPercent = sensor.SohPercent,
                    Voltage = sensor.Voltage,
                    Current = sensor.Current,
                    Temperature = sensor.Temperature,
                    SocPercent = sensor.SocPercent,
                    HasActiveAlert = sensor.HasActiveAlert,
                    // Ngưỡng thật của loại pin — thiếu chúng thì AI quay về hardcode và chấm
                    // bằng giới hạn khác với giới hạn backend đã áp.
                    TemperatureMax = sensor.TemperatureMax,
                    TemperatureMin = sensor.TemperatureMin,
                    SocWarningThreshold = sensor.SocWarningThreshold,
                    SohWarningThreshold = sensor.SohWarningThreshold
                };
            }

            foreach (var c in candidates)
            {
                request.Candidates.Add(new DuplicateCandidate
                {
                    TicketId = c.TicketId ?? string.Empty,
                    Description = c.Description ?? string.Empty,
                    Category = c.Category
                });
            }

            var deadline = DateTime.UtcNow.AddSeconds(_timeoutSeconds);
            var resp = await _client.VerifyTicketAsync(request, deadline: deadline, cancellationToken: ct);

            return new AiVerifyResult
            {
                Verdict = resp.Verdict,
                Score = resp.Score,
                Reason = resp.Reason,
                DuplicateOfTicketId = string.IsNullOrEmpty(resp.DuplicateOfTicketId) ? null : resp.DuplicateOfTicketId,
                DuplicateScore = resp.DuplicateScore,
                DuplicateReason = string.IsNullOrEmpty(resp.DuplicateReason) ? null : resp.DuplicateReason
            };
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "AI VerifyTicket RPC lỗi ({Status}) — verify skip.", ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI VerifyTicket lỗi — verify skip.");
            return null;
        }
    }
}
