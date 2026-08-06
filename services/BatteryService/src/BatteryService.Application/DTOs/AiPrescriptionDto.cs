namespace BatteryService.Application.DTOs;

/// <summary>
/// Prescription do AI sinh, trả thẳng cho UI khi kỹ thuật viên bấm "AI gợi ý chi tiết".
/// </summary>
public class AiPrescriptionDto
{
    /// <summary>Mô tả tổng. Rỗng khi AI chạy đường rule-based mà không có nội dung mô tả.</summary>
    public string Prescription { get; set; } = string.Empty;

    /// <summary>Các bước cụ thể — đã qua safety gate (LOTO/thermal được chèn nếu cần).</summary>
    public IReadOnlyList<string> ActionSteps { get; set; } = Array.Empty<string>();

    /// <summary>PPE bắt buộc.</summary>
    public IReadOnlyList<string> PpeRequired { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> SopReferences { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> SafetyWarnings { get; set; } = Array.Empty<string>();

    /// <summary>Điều kiện nên escalate — tham khảo, hệ thống KHÔNG tự escalate theo cái này.</summary>
    public IReadOnlyList<string> EscalationConditions { get; set; } = Array.Empty<string>();

    /// <summary>Luôn <c>true</c> với P1 và với mọi kết quả bị chặn.</summary>
    public bool HumanVerificationRequired { get; set; }

    /// <summary><c>true</c> = LLM+RAG đã chạy; <c>false</c> = bản rule-based.</summary>
    public bool Enriched { get; set; }

    /// <summary>"deepseek" / "gemini" / "anthropic" / "none".</summary>
    public string LlmProvider { get; set; } = "none";

    /// <summary>
    /// <c>true</c> khi output LLM bị safety gate CHẶN — nội dung dưới đây là bản rule-based
    /// thay thế, KHÔNG phải thứ LLM sinh ra. UI phải nói rõ điều này cho người đọc.
    /// </summary>
    public bool Blocked { get; set; }

    /// <summary>
    /// <c>true</c> khi AI trả từ cache idempotency (TTL 10 phút) thay vì chạy mới.
    /// </summary>
    /// <remarks>
    /// Bấm "gợi ý lại" hai lần liên tiếp với cùng dữ liệu sẽ ra cùng kết quả và <c>cached=true</c>
    /// — đó là đúng thiết kế, không phải nút bị hỏng. UI nên phân biệt để người dùng không bấm mãi.
    /// </remarks>
    public bool Cached { get; set; }

    /// <summary>
    /// ID để gửi phản hồi về <c>POST /api/alerts/{id}/prescription-feedback</c>.
    /// </summary>
    /// <remarks><c>null</c> khi AI không trả (đường rule-based hoặc history store lỗi).</remarks>
    public string? PrescriptionId { get; set; }
}
