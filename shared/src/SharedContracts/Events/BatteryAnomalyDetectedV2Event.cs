using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// V2 của <see cref="BatteryAnomalyDetectedEvent"/> — thêm trường cho Tier 2 monitoring
/// (Sprint 5B #105) + environmental incident scope (Sprint 5B #100).
///
/// Sprint 5B #237 — Saga subscribe cả V1 và V2 (xem overall.md §30.6).
///
/// Khác V1:
/// - <c>AssetSerialNumber</c> giờ nullable (cho EnvironmentalIncident scope site-level — không có asset).
/// - <c>SiteId</c> mới (site-level incident).
/// - <c>InternalResistanceMilliohm</c>, <c>CellVoltageDeltaMv</c> tier 2 (nullable).
/// - <c>EnvironmentalIncidentId</c> nullable (chỉ set khi anomaly type = EnvironmentalIncident).
/// </summary>
public record BatteryAnomalyDetectedV2Event(
    Guid AlertId,
    Guid? BatteryAssetId,
    Guid CustomerId,
    Guid? SiteId,
    string? AssetSerialNumber,
    int AnomalyType,
    int Severity,
    decimal ThresholdValue,
    decimal ActualValue,
    string Unit,
    DateTime DetectedAt,
    decimal? InternalResistanceMilliohm,
    decimal? CellVoltageDeltaMv,
    Guid? EnvironmentalIncidentId,
    // BE-AI — prescription text từ AI /prescribe (chỉ set khi SohPredictionBackgroundService
    // raise alert + PrescriptionEnabled). Nullable + CUỐI constructor để backward-compat:
    // consumer/saga cũ + threshold engine (không set) vẫn deserialize được (Saga #237 subscribe cả V1/V2).
    string? AiPrescription = null,
    IReadOnlyList<string>? AiActionSteps = null,
    // BE-AI structured — GIỮ NGUYÊN dạng có cấu trúc thay vì bóp thành text.
    //
    // `AiPrescription` ở trên là bản text đã ghép chuỗi (BuildPrescriptionText) và VẪN ĐƯỢC
    // GIỮ: saga nối nó vào Description để Manager đọc nhanh, và để mô tả tiếng Việt còn token
    // chung cho AI dò trùng ticket (xem comment trong SendCreateTicketActivity). Các field dưới
    // đây là BỔ SUNG, không thay thế — nhờ chúng TicketService mới lưu được vào
    // `ticket_ai_suggestions` và truy vấn/hiển thị theo từng mục.
    //
    // Vẫn theo đúng quy ước ở trên: nullable + thêm vào CUỐI ⇒ producer cũ (threshold engine)
    // và saga đã deploy vẫn deserialize bình thường.
    IReadOnlyList<string>? AiPpeRequired = null,
    IReadOnlyList<string>? AiSopReferences = null,
    IReadOnlyList<string>? AiEscalationConditions = null,
    IReadOnlyList<string>? AiSafetyWarnings = null,
    // Đường dẫn tài liệu KB mà AI truy hồi qua RAG (vd "maintenance/bms_warning_codes.md").
    // Gộp maintenance + safety: phía tiêu thụ chỉ cần biết AI đã tham chiếu tài liệu nào.
    IReadOnlyList<string>? AiKbDocRefs = null,
    bool? AiHumanVerificationRequired = null,
    // true = output LLM bị safety gate chặn, nội dung là bản rule-based fallback.
    bool? AiBlocked = null,
    bool? AiEnriched = null,
    string? AiLlmProvider = null,
    // ID để gửi phản hồi (accepted/edited/rejected) về AI — khép vòng học.
    string? AiPrescriptionId = null
) : IntegrationEvent;
