namespace BatteryService.Application.Interfaces;

/// <summary>GH-778 — kết quả gửi phản hồi prescription về AI.</summary>
public enum AiFeedbackOutcome
{
    /// <summary>AI đã ghi nhận.</summary>
    Recorded = 1,

    /// <summary>AI không biết <c>prescriptionId</c> này (404) — đã hết hạn hoặc chưa từng tồn tại.</summary>
    NotFound = 2,

    /// <summary>Không gọi được AI (mạng/timeout/5xx). Người gọi nên thử lại sau.</summary>
    Unavailable = 3,
}

/// <summary>
/// GH-778 — gửi phản hồi của kỹ thuật viên (accepted / edited / rejected) về AI.
/// </summary>
/// <remarks>
/// <para>
/// Tách khỏi <see cref="IAiPrescriptionClient"/> vì nó có vòng đời riêng: phản hồi đến SAU khi
/// kỹ thuật viên xử lý xong, không nằm trên đường dự đoán.
/// </para>
/// <para>
/// ⚠️ Ghi chú cũ ở đây từng khẳng định "<c>ai_service.proto</c> chỉ khai bốn RPC, KHÔNG có RPC
/// nào cho feedback, nên đây là ràng buộc THẬT". Điều đó KHÔNG CÒN ĐÚNG — proto nay đã có
/// <c>rpc SubmitFeedback</c>. Bản hiện thực là <c>FallbackAiPrescriptionFeedbackClient</c>
/// (gRPC primary → HTTP fallback), cùng khuôn với Predict/Prescribe.
/// </para>
/// <para>
/// Vì sao vòng phản hồi đáng có: prescription được AI chấp nhận sẽ thành ví dụ few-shot cho các ca
/// tương tự sau. Không có đường phản hồi thì AI lặp lại cùng lời khuyên sai mãi mà không ai sửa được.
/// </para>
/// </remarks>
public interface IAiPrescriptionFeedbackClient
{
    /// <param name="status">Chỉ nhận <c>accepted</c> | <c>edited</c> | <c>rejected</c> (hợp đồng AI).</param>
    /// <param name="editedSteps">Các bước đã sửa — chỉ có nghĩa khi <paramref name="status"/> = edited.</param>
    Task<AiFeedbackOutcome> SubmitFeedbackAsync(
        string prescriptionId,
        string status,
        IReadOnlyList<string>? editedSteps,
        string? note,
        CancellationToken cancellationToken = default);
}
