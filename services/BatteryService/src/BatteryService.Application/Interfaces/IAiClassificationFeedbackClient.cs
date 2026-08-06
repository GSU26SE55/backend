using BatteryService.Domain.Enums;

namespace BatteryService.Application.Interfaces;

/// <summary>
/// F4 — gửi phản hồi của Staff về PHÂN LOẠI anomaly (Normal/Degrading/Failed) ngược về AI.
/// </summary>
/// <remarks>
/// <para>
/// Khác <see cref="IAiPrescriptionFeedbackClient"/>: cái kia phản hồi về LỜI KHUYÊN bảo trì,
/// cái này phản hồi về NHÃN mà IsolationForest gán. Hai vòng học độc lập, hai store riêng.
/// </para>
/// <para>
/// Trước đây <c>staff_feedback</c> chỉ nằm lại trong <c>anomaly_classifications</c> của BE và
/// KHÔNG bao giờ đến được AI — nghĩa là AI phân loại sai bao nhiêu lần cũng không biết để sửa
/// ở lần retrain.
/// </para>
/// </remarks>
public interface IAiClassificationFeedbackClient
{
    /// <param name="classification">Nhãn AI ĐÃ đưa ra — không phải nhãn đúng.</param>
    /// <param name="feedback">Đánh giá của Staff về nhãn đó.</param>
    /// <returns>
    /// <c>true</c> nếu AI ghi nhận. <c>false</c> khi AI từ chối hoặc không gọi được —
    /// caller KHÔNG được vì thế mà fail: phản hồi đã lưu trong DB của BE rồi, mất đường gửi
    /// sang AI chỉ làm chậm vòng học chứ không làm hỏng thao tác của người dùng.
    /// </returns>
    Task<bool> SubmitAsync(
        Guid batteryAssetId,
        AnomalyClassificationEnum classification,
        StaffFeedbackEnum feedback,
        string modelVersion,
        DateTime classifiedAt,
        CancellationToken cancellationToken = default);
}
