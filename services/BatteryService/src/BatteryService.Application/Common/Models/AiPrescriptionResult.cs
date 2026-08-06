namespace BatteryService.Application.Common.Models;

/// <summary>
/// BE-AI — kết quả /prescribe/ (enrich=true) đã map về domain BE, transport-neutral.
/// gRPC + HTTP client cùng trả type này. Đổ vào ticket khi Alert P1/P2.
///
/// Field map (xem docs/overall-ai-be-integration.md §6.2):
///   Prescription               ← prescription (mô tả tổng)
///   ActionSteps                ← action_steps[] (đã qua safety gate — inject LOTO/thermal)
///   PpeRequired                ← ppe_required[] (đã union PPE bắt buộc)
///   SopReferences              ← sop_references[]
///   SafetyWarnings             ← safety_warnings[]
///   HumanVerificationRequired  ← human_verification_required (luôn true khi P1/blocked)
///   Enriched                   ← enriched (true = LLM chạy; false = rule-based fallback)
///   LlmProvider                ← llm_provider ("deepseek"/"gemini"/"none")
/// </summary>
public class AiPrescriptionResult
{
    public AiPrescriptionResult(
        string Prescription,
        IReadOnlyList<string> ActionSteps,
        IReadOnlyList<string> PpeRequired,
        IReadOnlyList<string> SopReferences,
        IReadOnlyList<string> SafetyWarnings,
        bool HumanVerificationRequired,
        bool Enriched,
        string LlmProvider,
        string? PrescriptionId = null,
        IReadOnlyList<string>? EscalationConditions = null,
        bool Blocked = false,
        bool Cached = false)
    {
        this.EscalationConditions = EscalationConditions ?? Array.Empty<string>();
        this.Blocked = Blocked;
        this.Cached = Cached;
        this.Prescription = Prescription;
        this.ActionSteps = ActionSteps;
        this.PpeRequired = PpeRequired;
        this.SopReferences = SopReferences;
        this.SafetyWarnings = SafetyWarnings;
        this.HumanVerificationRequired = HumanVerificationRequired;
        this.Enriched = Enriched;
        this.LlmProvider = LlmProvider;
        this.PrescriptionId = PrescriptionId;
    }

    public string Prescription { get; }
    public IReadOnlyList<string> ActionSteps { get; }
    public IReadOnlyList<string> PpeRequired { get; }
    public IReadOnlyList<string> SopReferences { get; }
    public IReadOnlyList<string> SafetyWarnings { get; }
    public bool HumanVerificationRequired { get; }
    public bool Enriched { get; }
    public string LlmProvider { get; }

    /// <summary>
    /// Điều kiện nên escalate ticket, do AI đề xuất (vd "SOH &lt; 70% ở lần đo tiếp theo").
    /// </summary>
    /// <remarks>
    /// AI trả field này từ đầu nhưng bridge chưa từng map ⇒ nó chết ngay tại đây. Đúng thứ
    /// TicketService cần để biết khi nào nâng cấp xử lý, mà lại đang phải tự đoán.
    /// Rỗng khi AI không trả (hoặc đường rule-based không sinh điều kiện nào).
    /// </remarks>
    public IReadOnlyList<string> EscalationConditions { get; }

    /// <summary>
    /// <c>true</c> khi output của LLM bị safety gate CHẶN — nội dung trả về là bản
    /// rule-based fallback, không phải thứ LLM sinh ra.
    /// </summary>
    /// <remarks>
    /// Không map field này nghĩa là một prescription đã bị chặn vì lý do an toàn lại được
    /// hiển thị y như prescription bình thường. Khi <c>Blocked=true</c>, AI luôn ép
    /// <see cref="HumanVerificationRequired"/>=true — nhưng UI vẫn nên nói rõ lý do.
    /// </remarks>
    public bool Blocked { get; }

    /// <summary>
    /// <c>true</c> khi response được phục vụ từ cache idempotency của AI (TTL 10 phút,
    /// khoá theo battery_id + readings + enrich + agentic + ticket_history).
    /// </summary>
    /// <remarks>
    /// Cần khi debug burst event: hai alert liên tiếp cho ra prescription giống hệt nhau là
    /// đúng thiết kế hay là bug? Không có field này thì không phân biệt được.
    /// Response <c>Blocked=true</c> KHÔNG BAO GIỜ được cache.
    /// </remarks>
    public bool Cached { get; }

    /// <summary>
    /// GH-778 — định danh do AI cấp cho prescription này, dùng để gửi phản hồi
    /// (accepted/edited/rejected) về <c>POST /prescribe/feedback</c>.
    /// </summary>
    /// <remarks>
    /// Proto có sẵn <c>prescription_id = 23</c> và AI có sẵn endpoint nhận phản hồi, nhưng cả hai
    /// client Battery đều BỎ trường này khi map ⇒ ID mất vĩnh viễn ngay tại ranh giới bridge. Hậu
    /// quả: kỹ thuật viên đọc được prescription nhưng không có cách nào nói lại nó đúng hay sai,
    /// nên vòng học của AI không bao giờ khép lại.
    /// <para>
    /// Null khi AI không trả (vd <c>enrich=false</c> — prescription theo luật, không qua LLM, nên
    /// không có gì để học).
    /// </para>
    /// </remarks>
    public string? PrescriptionId { get; }
}
