namespace TicketService.Infrastructure.Anomaly;

/// <summary>
/// Anomaly type CHỈ để báo cho khách, KHÔNG được sinh ticket.
///
/// Đây là lưới CHẶN THỨ HAI. Lưới thứ nhất nằm ở BatteryService
/// (<c>AnomalyRules.Detect</c>): LowSoc không còn được gán severity Critical, mà chỉ alert
/// Critical mới publish <c>BatteryAnomalyDetectedEvent</c>/<c>V2</c> — hai event duy nhất dẫn
/// tới ticket. Nên trong cấu hình hiện tại lưới này không bao giờ phải chặn gì.
///
/// Vẫn giữ vì lưới thứ nhất nằm ở REPO KHÁC SERVICE và chỉ là một hằng severity: ai đó nâng
/// LowSoc về Critical, hoặc một đường publish mới xuất hiện, là ticket rác quay lại mà không
/// có gì báo. Danh sách này làm ý định "LowSoc không đẻ ticket" hiện ra ngay trong TicketService.
///
/// Dùng <c>int</c> chứ không phải enum: <c>AnomalyTypeEnum</c> thuộc BatteryService.Domain,
/// TicketService không tham chiếu tới. Cùng quy ước với các bảng map <c>anomalyType switch</c>
/// trong <c>SendCreateTicketActivity</c> và <c>TicketBatteryAnomalyDetectedConsumer</c>.
/// </summary>
public static class NotificationOnlyAnomalies
{
    /// <summary>BatteryService <c>AnomalyTypeEnum.LowSoc</c>.</summary>
    public const int LowSoc = 4;

    private static readonly HashSet<int> Types = [LowSoc];

    /// <summary>
    /// True nếu anomaly này chỉ được phép đi tới notification.
    /// Pin xả cạn là vận hành bình thường của hệ solar, không phải sự cố cần người xử lý.
    /// </summary>
    public static bool Contains(int anomalyType) => Types.Contains(anomalyType);
}
