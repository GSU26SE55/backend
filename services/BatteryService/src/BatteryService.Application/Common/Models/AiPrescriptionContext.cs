namespace BatteryService.Application.Common.Models;

/// <summary>
/// BE-AI — ngữ cảnh lịch sử pin gửi kèm <c>Prescribe</c>. CHỈ có tác dụng khi <c>enrich=true</c>.
/// </summary>
/// <remarks>
/// <para>
/// AI nhận sẵn ba field này từ lâu nhưng bridge chưa bao giờ gửi, nên LLM luôn kê đơn cho một
/// viên pin "không có quá khứ": không biết pin đã chạy bao nhiêu chu kỳ, lần bảo trì gần nhất
/// khi nào, hay trước đó đã sửa những gì.
/// </para>
/// <para>
/// Đường <c>enrich=false</c> KHÔNG dùng nội dung này (nhưng nó vẫn nằm trong khoá dedup của AI,
/// nên hai request khác lịch sử sẽ không dùng chung cache — đó là chủ ý).
/// </para>
/// </remarks>
public class AiPrescriptionContext
{
    public AiPrescriptionContext(
        int? AgeCycles,
        string? LastMaintenanceDate,
        IReadOnlyList<string> TicketHistory)
    {
        this.AgeCycles = AgeCycles;
        this.LastMaintenanceDate = LastMaintenanceDate;
        this.TicketHistory = TicketHistory;
    }

    /// <summary>Số chu kỳ sạc-xả pin đã trải qua, lấy từ <c>cycle_count</c> của mẫu mới nhất.</summary>
    public int? AgeCycles { get; }

    /// <summary>
    /// Ngày bảo trì gần nhất, dạng ISO (<c>yyyy-MM-dd</c>). <c>null</c> nếu chưa từng có.
    /// </summary>
    /// <remarks>
    /// BatteryService không có bảng maintenance log riêng, nên mốc này lấy từ Alert đã RESOLVE
    /// gần nhất của chính pin đó — đúng nghĩa "lần cuối có người đụng vào pin này".
    /// </remarks>
    public string? LastMaintenanceDate { get; }

    /// <summary>
    /// Tóm tắt các lần sửa chữa trước, mỗi phần tử 1 dòng, thứ tự CŨ → MỚI.
    /// </summary>
    /// <remarks>
    /// ⚠️ Thứ tự cũ→mới là bắt buộc: AI chỉ lấy 5 phần tử CUỐI làm context. Gửi ngược chiều
    /// nghĩa là đưa cho LLM 5 sự kiện xa nhất thay vì gần nhất — sai mà không có lỗi nào báo.
    /// Nguồn dữ liệu là alert đã resolve của BatteryService, không phải ticket của TicketService
    /// (BatteryService không gọi sang service đó).
    /// </remarks>
    public IReadOnlyList<string> TicketHistory { get; }

    /// <summary>Ngữ cảnh rỗng — dùng khi caller không có lịch sử để gửi.</summary>
    public static AiPrescriptionContext Empty { get; } =
        new(AgeCycles: null, LastMaintenanceDate: null, TicketHistory: Array.Empty<string>());
}
