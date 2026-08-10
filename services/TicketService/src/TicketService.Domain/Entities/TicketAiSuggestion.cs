using SharedKernels.Domain;

namespace TicketService.Domain.Entities;

/// <summary>
/// Gợi ý của AI cho một Ticket được tạo tự động từ Alert — dạng CÓ CẤU TRÚC.
///
/// Quan hệ 1-1 với <see cref="Ticket"/>. Chỉ tồn tại với ticket
/// <c>Origin = AutoFromAlert</c> có chạy prescription; ticket do Customer tạo hoặc
/// ticket từ threshold engine (không gọi AI) sẽ KHÔNG có bản ghi nào.
/// </summary>
/// <remarks>
/// Trước đây toàn bộ output của AI bị ghép thành một chuỗi rồi nối vào
/// <c>Ticket.Description</c>. Hệ quả: không truy vấn được, FE không tách được thành
/// panel riêng, và <c>sop_references</c>/<c>maintenance_docs</c> mất hẳn.
/// Bảng này giữ nguyên cấu trúc để dùng lại được — đặc biệt là <see cref="KbDocRefs"/>
/// cho việc gợi ý tài liệu KB khi kỹ thuật viên sửa chữa.
/// <para>
/// Đoạn text trong <c>Ticket.Description</c> VẪN giữ (Manager đọc nhanh + AI dò trùng
/// ticket cần token tiếng Việt) — bảng này là BỔ SUNG, không thay thế.
/// </para>
/// </remarks>
public class TicketAiSuggestion : AuditableEntity
{
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    /// <summary>Mô tả tổng của prescription (bản gốc, chưa ghép chuỗi).</summary>
    public string Prescription { get; set; } = string.Empty;

    /// <summary>Các bước xử lý — đã qua safety gate của AI (inject LOTO/thermal).</summary>
    public List<string> ActionSteps { get; set; } = new();

    /// <summary>Trang bị bảo hộ bắt buộc — đã union với PPE theo luật.</summary>
    public List<string> PpeRequired { get; set; } = new();

    /// <summary>Quy trình chuẩn (SOP) mà AI tham chiếu.</summary>
    public List<string> SopReferences { get; set; } = new();

    /// <summary>Điều kiện nên nâng cấp xử lý, vd "SOH &lt; 70% ở lần đo tiếp theo".</summary>
    public List<string> EscalationConditions { get; set; } = new();

    /// <summary>Cảnh báo an toàn kèm theo.</summary>
    public List<string> SafetyWarnings { get; set; } = new();

    /// <summary>
    /// Đường dẫn tài liệu KB mà AI truy hồi được qua RAG
    /// (vd <c>"maintenance/bms_warning_codes.md"</c>) — gộp maintenance + safety.
    /// </summary>
    /// <remarks>
    /// Đây là tín hiệu MẠNH NHẤT để gợi ý KB: tài liệu AI đã thực sự đọc khi kê đơn,
    /// đáng tin hơn nhiều so với đoán từ khoá từ mô tả ticket.
    /// <para>
    /// ⚠️ Là đường dẫn file trong knowledge base của ai-module, KHÔNG phải
    /// <c>KnowledgeBaseArticle.Code</c>. Muốn map sang bài viết KB của hệ thống cần
    /// đối chiếu theo tiêu đề/nội dung — xem phần gợi ý KB.
    /// </para>
    /// </remarks>
    public List<string> KbDocRefs { get; set; } = new();

    /// <summary>Bắt buộc người kiểm chứng trước khi thao tác (luôn true khi P1 hoặc bị chặn).</summary>
    public bool HumanVerificationRequired { get; set; }

    /// <summary>
    /// <c>true</c> khi output của LLM bị safety gate CHẶN — nội dung ở đây là bản
    /// rule-based fallback, không phải thứ LLM sinh ra. UI nên nói rõ điều này.
    /// </summary>
    public bool Blocked { get; set; }

    /// <summary><c>true</c> = có chạy RAG + LLM; <c>false</c> = chỉ theo luật.</summary>
    public bool Enriched { get; set; }

    /// <summary>"deepseek" / "gemini" / "anthropic" / "none".</summary>
    public string LlmProvider { get; set; } = "none";

    /// <summary>
    /// ID do AI cấp, dùng để gửi phản hồi (accepted/edited/rejected) về AI — khép vòng học.
    /// Null khi AI chạy đường rule-based (không có gì để học).
    /// </summary>
    public string? PrescriptionId { get; set; }
}
