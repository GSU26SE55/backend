using System.Net.Http.Json;
using System.Text.Json;
using BatteryService.Application.Common.Models;
using BatteryService.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace BatteryService.Infrastructure.Implements.Ai;

/// <summary>
/// BE-AI — HTTP/FastAPI impl của Prescribe (FALLBACK). POST /prescribe/ (⚠️ dấu "/" cuối) trên :8000.
/// </summary>
public class AiPrescriptionHttpClient : IAiPrescriptionFeedbackClient
{
    private readonly HttpClient _http;
    private readonly ILogger<AiPrescriptionHttpClient> _logger;

    public AiPrescriptionHttpClient(HttpClient http, ILogger<AiPrescriptionHttpClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public virtual async Task<AiPrescriptionResult?> PrescribeAsync(
        string batteryId,
        IReadOnlyList<double[]> readings,
        bool enrich,
        AiPackConfig? packConfig,
        CancellationToken cancellationToken,
        AiPrescriptionContext? context = null,
        bool agentic = false)
    {
        // Dựng payload bằng Dictionary thay vì anonymous type: số nhánh tổ hợp của
        // (packConfig có/không) × (context có/không) tăng nhanh, mà mỗi nhánh lại phải lặp lại
        // toàn bộ field — đúng chỗ dễ bỏ sót một field ở đúng một nhánh.
        var payload = new Dictionary<string, object?>
        {
            ["battery_id"] = batteryId,
            ["readings"] = readings,
            ["enrich"] = enrich,
            ["agentic"] = agentic,
        };
        if (packConfig is not null)
        {
            payload["pack_config"] = new
            {
                n_series = packConfig.NSeries,
                chemistry = packConfig.Chemistry,
                capacity_ah = packConfig.CapacityAh,
            };
        }
        if (context is not null)
        {
            if (context.AgeCycles is int age) payload["age_cycles"] = age;
            if (!string.IsNullOrEmpty(context.LastMaintenanceDate))
                payload["last_maintenance_date"] = context.LastMaintenanceDate;
            // Thứ tự CŨ → MỚI — AI chỉ lấy 5 phần tử cuối.
            if (context.TicketHistory.Count > 0)
                payload["ticket_history"] = context.TicketHistory;
        }

        using var response = await _http.PostAsJsonAsync("/prescribe/", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "AI /prescribe HTTP non-success {Status} for {BatteryId}: {Body}",
                response.StatusCode, batteryId, body);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        static string Str(JsonElement e, string p) =>
            e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : string.Empty;
        static bool Bool(JsonElement e, string p) =>
            e.TryGetProperty(p, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) && v.GetBoolean();
        static List<string> StrList(JsonElement e, string p)
        {
            var list = new List<string>();
            if (e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Array)
                foreach (var item in v.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String)
                        list.Add(item.GetString()!);
            return list;
        }
        // Parity với gRPC: bỏ `content`, chỉ giữ title/source/relevance_score.
        static List<AiRetrievedDoc> DocList(JsonElement e, string p)
        {
            var list = new List<AiRetrievedDoc>();
            if (e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Array)
                foreach (var item in v.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.Object)
                        list.Add(new AiRetrievedDoc(
                            Str(item, "title"),
                            Str(item, "source"),
                            item.TryGetProperty("relevance_score", out var s) && s.ValueKind == JsonValueKind.Number
                                ? s.GetDouble()
                                : 0d));
            return list;
        }

        return new AiPrescriptionResult(
            Prescription: Str(root, "prescription"),
            ActionSteps: StrList(root, "action_steps"),
            PpeRequired: StrList(root, "ppe_required"),
            SopReferences: StrList(root, "sop_references"),
            SafetyWarnings: StrList(root, "safety_warnings"),
            HumanVerificationRequired: Bool(root, "human_verification_required"),
            Enriched: Bool(root, "enriched"),
            LlmProvider: Str(root, "llm_provider"),
            // GH-778 — xem AiPrescriptionResult.PrescriptionId.
            PrescriptionId: Str(root, "prescription_id") is { Length: > 0 } id ? id : null,
            EscalationConditions: StrList(root, "escalation_conditions"),
            Blocked: Bool(root, "blocked"),
            Cached: Bool(root, "cached"),
            MaintenanceDocs: DocList(root, "maintenance_docs"),
            SafetyDocs: DocList(root, "safety_docs"));
    }
    /// <summary>
    /// GH-778 — gửi phản hồi kỹ thuật viên về AI. Chỉ có ở đường HTTP: <c>ai_service.proto</c>
    /// không khai RPC nào cho feedback (xem <see cref="IAiPrescriptionFeedbackClient"/>).
    /// </summary>
    public virtual async Task<AiFeedbackOutcome> SubmitFeedbackAsync(
        string prescriptionId,
        string status,
        IReadOnlyList<string>? editedSteps,
        string? note,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                prescription_id = prescriptionId,
                status,
                edited_steps = editedSteps,
                note,
            };

            using var resp = await _http.PostAsJsonAsync("/prescribe/feedback", payload, cancellationToken);

            // 404 = AI không biết id này (hết hạn hoặc chưa từng có). Đây KHÔNG phải lỗi hạ tầng:
            // thử lại cũng vô ích, nên phải phân biệt với Unavailable để người gọi khỏi retry vô nghĩa.
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning(
                    "[AiPrescriptionFeedback] AI không biết prescriptionId {Id} — bỏ qua, không retry.",
                    prescriptionId);
                return AiFeedbackOutcome.NotFound;
            }

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[AiPrescriptionFeedback] AI trả {Status} cho prescriptionId {Id}.",
                    (int)resp.StatusCode, prescriptionId);
                return AiFeedbackOutcome.Unavailable;
            }

            return AiFeedbackOutcome.Recorded;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Phản hồi là dữ liệu để AI học, KHÔNG phải nghiệp vụ chặn đường. AI sập thì báo
            // Unavailable để người gọi thử lại — chứ không ném lỗi ra tận người dùng cuối.
            _logger.LogWarning(ex, "[AiPrescriptionFeedback] Không gọi được AI cho prescriptionId {Id}.", prescriptionId);
            return AiFeedbackOutcome.Unavailable;
        }
    }

}
