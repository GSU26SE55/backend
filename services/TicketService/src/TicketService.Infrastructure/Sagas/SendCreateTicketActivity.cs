using System.Text.Json;
using MassTransit;
using SharedContracts.Saga.AlertTicket;

namespace TicketService.Infrastructure.Sagas;

/// <summary>
/// Activity gửi <see cref="CreateTicketFromAlertCommand"/> đến TicketService consumer.
/// Publish qua transactional Outbox của MassTransit (xem #235 EF Consumer Outbox).
///
/// Sprint 5B #237.
/// </summary>
public class SendCreateTicketActivity : IStateMachineActivity<AlertTicketSagaState>
{
    public void Probe(ProbeContext context) => context.CreateScope("send-create-ticket");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(BehaviorContext<AlertTicketSagaState> context, IBehavior<AlertTicketSagaState> next)
    {
        await PublishCreateTicketAsync(context, context.Saga);
        await next.Execute(context).ConfigureAwait(false);
    }

    /// <summary>
    /// FIX saga-stuck: MassTransit gọi overload GENERIC này khi activity chạy trong behavior
    /// có message data — chính là <c>Initially(When(AnomalyDetectedV1/V2))</c>. Trước đây nó chỉ
    /// <c>next.Execute(context)</c> mà KHÔNG publish command → saga chuyển sang TicketRequested
    /// nhưng CreateTicketFromAlertCommand không bao giờ được gửi → saga kẹt vĩnh viễn, không
    /// ticket auto nào được tạo. Nay publish giống overload non-generic.
    /// </summary>
    public async Task Execute<T>(BehaviorContext<AlertTicketSagaState, T> context, IBehavior<AlertTicketSagaState, T> next)
        where T : class
    {
        await PublishCreateTicketAsync(context, context.Saga);
        await next.Execute(context).ConfigureAwait(false);
    }

    /// <summary>Build + publish <see cref="CreateTicketFromAlertCommand"/> từ state saga.</summary>
    private static async Task PublishCreateTicketAsync(IPublishEndpoint publishEndpoint, AlertTicketSagaState saga)
    {
        // Mô tả tiếng Việt, nêu rõ thông số + nguyên nhân theo loại anomaly.
        // Trước đây là câu tiếng Anh toàn số ("Anomaly detected at ... Value: ... Threshold: ...")
        // → KHÔNG có token chung nào với mô tả Customer viết tiếng Việt ⇒ Jaccard = 0 ⇒ AI dò
        // trùng không bao giờ phát hiện được cặp (ticket auto ↔ ticket thủ công) trên cùng pin.
        var description =
            $"{DescribeAnomaly(saga.AnomalyType)} " +
            $"Giá trị đo được {saga.ActualValue} {saga.Unit}, vượt ngưỡng cho phép {saga.ThresholdValue} {saga.Unit}. " +
            $"Phát hiện lúc {saga.DetectedAt:HH:mm dd/MM/yyyy} (UTC) trên thiết bị {saga.AssetSerialNumber}.";
        // BE-AI — nếu AI có prescription (chỉ V2 từ SohPredictionBackgroundService), ghép vào
        // Description để Manager thấy khuyến nghị ngay khi ticket vào hàng chờ. Nullable → không đổi
        // ticket từ threshold engine (AiPrescription = null).
        if (!string.IsNullOrWhiteSpace(saga.AiPrescription))
            description += $"\n\n--- AI Prescription ---\n{saga.AiPrescription}";

        // BE-AI structured — mở gói để forward sang TicketService (đích: `ticket_ai_suggestions`).
        // Best-effort: JSON hỏng KHÔNG được làm chết việc tạo ticket — mất gợi ý còn hơn mất ticket.
        AiSuggestionPayload? ai = null;
        if (!string.IsNullOrWhiteSpace(saga.AiSuggestionJson))
        {
            try
            {
                ai = JsonSerializer.Deserialize<AiSuggestionPayload>(saga.AiSuggestionJson);
            }
            catch (JsonException)
            {
                ai = null;
            }
        }

        var command = new CreateTicketFromAlertCommand(
            CorrelationId: saga.CorrelationId,
            AlertId: saga.AlertId,
            BatteryAssetId: saga.BatteryAssetId ?? Guid.Empty,
            CustomerId: saga.CustomerId,
            AssetSerialNumber: saga.AssetSerialNumber ?? string.Empty,
            AnomalyType: saga.AnomalyType,
            Severity: saga.Severity,
            ThresholdValue: saga.ThresholdValue,
            ActualValue: saga.ActualValue,
            Unit: saga.Unit,
            DetectedAt: saga.DetectedAt,
            AnomalyCategory: MapAnomalyTypeToCategory(saga.AnomalyType),
            Title: $"[Auto] {saga.AssetSerialNumber ?? "Battery"} - {MapAnomalyTypeToTitle(saga.AnomalyType)}",
            Description: description,
            AiPrescription: saga.AiPrescription,
            AiActionSteps: ai?.ActionSteps,
            AiPpeRequired: ai?.PpeRequired,
            AiSopReferences: ai?.SopReferences,
            AiEscalationConditions: ai?.EscalationConditions,
            AiSafetyWarnings: ai?.SafetyWarnings,
            AiKbDocRefs: ai?.KbDocRefs,
            AiHumanVerificationRequired: ai?.HumanVerificationRequired,
            AiBlocked: ai?.Blocked,
            AiEnriched: ai?.Enriched,
            AiLlmProvider: ai?.LlmProvider,
            AiPrescriptionId: ai?.PrescriptionId
        );

        await publishEndpoint.Publish(command);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<AlertTicketSagaState, TException> context, IBehavior<AlertTicketSagaState> next)
        where TException : Exception
        => next.Faulted(context);

    public Task Faulted<T, TException>(BehaviorExceptionContext<AlertTicketSagaState, T, TException> context, IBehavior<AlertTicketSagaState, T> next)
        where T : class
        where TException : Exception
        => next.Faulted(context);

    /// <summary>
    /// Câu mô tả nguyên nhân theo loại anomaly — tiếng Việt, dùng từ ngữ Customer thường gõ
    /// (nóng/quá nhiệt, sụt áp, sạc, chai pin…) để AI dò trùng so được mô tả 2 nguồn ticket.
    /// </summary>
    private static string DescribeAnomaly(int anomalyType) => anomalyType switch
    {
        1 => "Pin quá nhiệt — cảm biến ghi nhận nhiệt độ pin vượt ngưỡng an toàn.",
        2 => "Pin quá áp — điện áp vượt ngưỡng cho phép.",
        3 => "Pin sụt áp — điện áp tụt dưới ngưỡng an toàn.",
        4 => "Pin cạn — mức pin (SOC) xuống rất thấp.",
        5 => "Pin xả nhanh bất thường — tốc độ xả vượt ngưỡng.",
        6 => "Sạc bất thường — dòng sạc vượt ngưỡng cho phép.",
        7 => "Thiết bị mất kết nối — không nhận được dữ liệu cảm biến.",
        8 => "Pin chai, suy giảm sức khoẻ (SOH) — hiệu suất giảm dưới ngưỡng.",
        9 => "Nhiệt độ môi trường quanh pin cao bất thường.",
        10 => "Độ ẩm quanh pin cao bất thường.",
        11 => "Nhiệt độ và độ ẩm quanh pin cùng cao bất thường.",
        12 => "Điện trở trong của pin tăng cao — dấu hiệu pin xuống cấp.",
        13 => "Lệch áp giữa các cell pin vượt ngưỡng.",
        14 => "Sự cố môi trường tại site ảnh hưởng tới pin.",
        15 => "Cảm biến báo số liệu không khớp — nghi ngờ lỗi cảm biến.",
        _ => "Phát hiện bất thường trên pin."
    };

    private static string MapAnomalyTypeToCategory(int anomalyType) => anomalyType switch
    {
        1 => "Overheat",
        2 => "Overvoltage",
        3 => "Undervoltage",
        4 => "LowSoc",
        5 => "RapidDischarge",
        6 => "AbnormalCharging",
        7 => "DeviceOffline",
        8 => "SohDegradation",
        9 => "HighAmbientTemp",
        10 => "HighHumidity",
        11 => "HighTempHumidityCombo",
        12 => "HighInternalResistance",
        13 => "CellImbalance",
        14 => "EnvironmentalIncident",
        15 => "SensorMismatch",
        _ => "GenericAnomaly"
    };

    private static string MapAnomalyTypeToTitle(int anomalyType) => anomalyType switch
    {
        1 => "Battery Overheating",
        2 => "Overvoltage Detected",
        3 => "Undervoltage Detected",
        4 => "Low SOC Warning",
        5 => "Rapid Discharge",
        6 => "Abnormal Charging",
        7 => "Device Offline",
        8 => "SOH Degradation Alert",
        9 => "High Ambient Temperature",
        10 => "High Humidity",
        11 => "High Temperature + Humidity",
        12 => "High Internal Resistance",
        13 => "Cell Voltage Imbalance",
        14 => "Environmental Incident",
        15 => "Sensor Mismatch (BMS vs IoT)",
        _ => "Critical Battery Anomaly"
    };
}
