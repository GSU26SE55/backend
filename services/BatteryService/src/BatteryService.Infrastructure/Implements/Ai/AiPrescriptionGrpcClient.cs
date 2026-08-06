using AiModule.V1;
using BatteryService.Application.Common.Models;

namespace BatteryService.Infrastructure.Implements.Ai;

/// <summary>
/// BE-AI — gRPC impl của Prescribe (PRIMARY). Gọi AiService.Prescribe trên :50051.
/// Ném RpcException lên FallbackAiPrescriptionClient (same fallback policy as Predict).
/// </summary>
public class AiPrescriptionGrpcClient
{
    private readonly AiService.AiServiceClient _client;

    public AiPrescriptionGrpcClient(AiService.AiServiceClient client) => _client = client;

    public virtual async Task<AiPrescriptionResult> PrescribeAsync(
        string batteryId,
        IReadOnlyList<double[]> readings,
        bool enrich,
        AiPackConfig? packConfig,
        int timeoutSeconds,
        CancellationToken cancellationToken,
        AiPrescriptionContext? context = null,
        bool agentic = false)
    {
        var request = new PrescribeRequest
        {
            BatteryId = batteryId,
            Enrich = enrich,
            Agentic = agentic,
        };
        if (context is not null)
        {
            if (context.AgeCycles is int age)
                request.AgeCycles = age;
            if (!string.IsNullOrEmpty(context.LastMaintenanceDate))
                request.LastMaintenanceDate = context.LastMaintenanceDate;
            // Thứ tự CŨ → MỚI: AI chỉ lấy 5 phần tử CUỐI. Gửi ngược chiều là đưa cho LLM
            // 5 sự kiện xa nhất thay vì gần nhất — sai mà không có lỗi nào báo.
            request.TicketHistory.AddRange(context.TicketHistory);
        }
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

        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var resp = await _client.PrescribeAsync(
            request, deadline: deadline, cancellationToken: cancellationToken);

        return new AiPrescriptionResult(
            Prescription: resp.Prescription,
            ActionSteps: resp.ActionSteps.ToList(),
            PpeRequired: resp.PpeRequired.ToList(),
            SopReferences: resp.SopReferences.ToList(),
            SafetyWarnings: resp.SafetyWarnings.ToList(),
            HumanVerificationRequired: resp.HumanVerificationRequired,
            Enriched: resp.Enriched,
            LlmProvider: resp.LlmProvider,
            // GH-778 — giữ lại ID để còn gửi phản hồi được. Bỏ ở đây là cắt đứt vòng học của AI
            // ngay tại ranh giới bridge.
            PrescriptionId: string.IsNullOrWhiteSpace(resp.PrescriptionId) ? null : resp.PrescriptionId,
            EscalationConditions: resp.EscalationConditions.ToList(),
            Blocked: resp.Blocked,
            Cached: resp.Cached);
    }
}
