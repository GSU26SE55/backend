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
        string? PrescriptionId = null)
    {
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
