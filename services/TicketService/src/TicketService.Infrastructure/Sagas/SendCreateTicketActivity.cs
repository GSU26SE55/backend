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
            $"Measured value {saga.ActualValue} {saga.Unit}, exceeding the allowed threshold {saga.ThresholdValue} {saga.Unit}. " +
            $"Detected at {saga.DetectedAt:HH:mm dd/MM/yyyy} (UTC) on device {saga.AssetSerialNumber}.";
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
            AiPrescriptionId: ai?.PrescriptionId,
            // Saga đã hydrate SiteId từ event V2 — forward xuống để ticket lưu được site.
            SiteId: saga.SiteId
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
        1 => "Battery overheating — the sensor recorded a battery temperature above the safe threshold.",
        2 => "Battery overvoltage — the voltage exceeded the allowed threshold.",
        3 => "Battery undervoltage — the voltage dropped below the safe threshold.",
        4 => "Battery nearly empty — the state of charge (SOC) dropped very low.",
        5 => "Abnormally rapid discharge — the discharge rate exceeded the threshold.",
        6 => "Abnormal charging — the charging current exceeded the allowed threshold.",
        7 => "Device offline — no sensor data has been received.",
        8 => "Battery degradation, declining state of health (SOH) — performance dropped below the threshold.",
        9 => "Abnormally high ambient temperature around the battery.",
        10 => "Abnormally high humidity around the battery.",
        11 => "Both temperature and humidity around the battery are abnormally high.",
        12 => "Battery internal resistance has risen — a sign of degradation.",
        13 => "Voltage imbalance between battery cells exceeded the threshold.",
        14 => "An environmental incident at the site is affecting the battery.",
        15 => "Sensor readings do not match — a sensor fault is suspected.",
        _ => "An anomaly was detected on the battery."
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
        // 16/17 thiếu ở đây khiến chúng ra "GenericAnomaly", mà chuỗi đó không khớp nhánh nào
        // trong `MapAnomalyToCategory` nên rơi vào `_ => Repair`. Hệ quả không chỉ là nhãn sai:
        // mọi anomaly lạ đều dồn về CÙNG một category, tranh nhau slot của unique index
        // `ux_tickets_active_auto_per_asset_category` (asset, category) — cái vào trước chiếm
        // chỗ, cái sau không insert được và saga kẹt ở `TicketRequested` mà không log gì.
        16 => "Undertemp",
        17 => "IotDataIntegrityViolation",
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
        // Thiếu 16/17 ở đây là lý do ticket Undertemp mang tiêu đề "Critical Battery Anomaly" —
        // đọc lên không biết pin bị lỗi gì, và cũng không phân biệt được với mọi loại lạ khác.
        16 => "Low Temperature",
        17 => "IoT Data Integrity Violation",
        _ => "Critical Battery Anomaly"
    };
}
