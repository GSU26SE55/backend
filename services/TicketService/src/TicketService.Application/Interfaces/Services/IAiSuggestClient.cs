using TicketService.Application.Common.Models;

namespace TicketService.Application.Interfaces.Services;

/// <summary>
/// BE-AI — gọi AI xếp hạng nhân viên phù hợp xử lý ticket.
/// </summary>
/// <remarks>
/// Human-in-the-loop: AI chỉ gợi ý + nêu lý do; Manager quyết định giao cho ai và vẫn
/// có thể chọn người ngoài danh sách. Không có lệnh phân công nào ở đây.
/// </remarks>
public interface IAiStaffSuggestClient
{
    /// <summary>
    /// Trả về danh sách đã xếp hạng, hoặc <c>null</c> khi AI không phản hồi được.
    /// </summary>
    /// <remarks>
    /// <c>null</c> nghĩa là "không gợi ý được", KHÔNG phải "không có ai phù hợp" —
    /// người gọi phải phân biệt hai trường hợp này khi hiển thị.
    /// </remarks>
    Task<AiStaffSuggestResult?> SuggestStaffAsync(
        int category,
        int priority,
        string description,
        IReadOnlyList<AiStaffCandidate> candidates,
        int topN,
        CancellationToken ct);
}

/// <summary>
/// BE-AI — gọi AI xếp hạng bài viết KB để kỹ thuật viên tham khảo khi sửa chữa.
/// </summary>
/// <remarks>
/// Human-in-the-loop: kỹ thuật viên bấm áp dụng thì mới tạo <c>TicketKbReference</c>.
/// </remarks>
public interface IAiKbSuggestClient
{
    /// <summary>
    /// Trả về danh sách đã xếp hạng, hoặc <c>null</c> khi AI không phản hồi được.
    /// </summary>
    /// <param name="aiActionSteps">Các bước AI đã đề xuất cho chính ticket này.</param>
    /// <param name="aiSopReferences">SOP AI tham chiếu — tín hiệu khớp mạnh.</param>
    /// <param name="aiKbDocRefs">Tài liệu AI truy hồi qua RAG — tín hiệu mạnh nhất.</param>
    Task<AiKbSuggestResult?> SuggestKbAsync(
        int category,
        string description,
        IReadOnlyList<AiKbCandidate> candidates,
        int topN,
        IReadOnlyList<string> aiActionSteps,
        IReadOnlyList<string> aiSopReferences,
        IReadOnlyList<string> aiKbDocRefs,
        CancellationToken ct);
}
