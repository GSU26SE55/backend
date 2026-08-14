namespace SharedContracts.Saga.AlertTicket;

/// <summary>
/// BE-AI structured — gói gọn phần gợi ý của AI khi đi qua saga.
///
/// Dùng ở hai chỗ:
///   1. <c>AlertTicketSagaState.AiSuggestionJson</c> — serialize để lưu tạm 1 cột JSON.
///   2. Dựng lại <c>CreateTicketFromAlertCommand</c> khi saga publish sang TicketService.
///
/// Đích cuối là bảng <c>ticket_ai_suggestions</c>, nơi mới thực sự truy vấn theo từng mục.
/// </summary>
/// <remarks>
/// KHÔNG chứa phần text đã ghép chuỗi (<c>AiPrescription</c>) — cái đó đi đường riêng vì saga
/// còn nối nó vào <c>Description</c> của ticket (xem <c>SendCreateTicketActivity</c>).
/// </remarks>
public record AiSuggestionPayload(
    IReadOnlyList<string>? ActionSteps = null,
    IReadOnlyList<string>? PpeRequired = null,
    IReadOnlyList<string>? SopReferences = null,
    IReadOnlyList<string>? EscalationConditions = null,
    IReadOnlyList<string>? SafetyWarnings = null,
    IReadOnlyList<string>? KbDocRefs = null,
    bool? HumanVerificationRequired = null,
    bool? Blocked = null,
    bool? Enriched = null,
    string? LlmProvider = null,
    string? PrescriptionId = null)
{
    /// <summary>
    /// <c>true</c> khi không có bất kỳ tín hiệu AI nào — dùng để bỏ qua việc ghi
    /// <c>ticket_ai_suggestions</c> cho ticket từ threshold engine (không gọi AI).
    /// </summary>
    public bool IsEmpty =>
        (ActionSteps is null or { Count: 0 })
        && (PpeRequired is null or { Count: 0 })
        && (SopReferences is null or { Count: 0 })
        && (EscalationConditions is null or { Count: 0 })
        && (SafetyWarnings is null or { Count: 0 })
        && (KbDocRefs is null or { Count: 0 })
        && string.IsNullOrWhiteSpace(PrescriptionId);
}
