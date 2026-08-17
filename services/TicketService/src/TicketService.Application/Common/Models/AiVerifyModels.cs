namespace TicketService.Application.Common.Models;

/// <summary>
/// Snapshot sensor pin tại thời điểm phát hiện — để AI đối chiếu mô tả với thực tế.
/// Null nếu consumer không lấy được (verify vẫn chạy bằng heuristic text).
/// </summary>
public class TicketSensorSnapshotDto
{
    public double SohPercent { get; set; }
    public double Voltage { get; set; }
    public double Current { get; set; }
    public double Temperature { get; set; }
    public double SocPercent { get; set; }
    public bool HasActiveAlert { get; set; }

    /// <summary>
    /// Ngưỡng của loại pin, đi kèm số đo để AI verify chấm bằng đúng giới hạn mà
    /// <c>AnomalyRules</c> đã áp. 0 = chưa cấu hình ⇒ AI bỏ qua luật tương ứng.
    /// </summary>
    public double TemperatureMax { get; set; }
    public double TemperatureMin { get; set; }
    public double SocWarningThreshold { get; set; }
    public double SohWarningThreshold { get; set; }
}

/// <summary>1 ticket ứng viên để so trùng mô tả (đang mở, cùng pin).</summary>
public class DuplicateCandidateDto
{
    public string TicketId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Category { get; set; }

    /// <summary>Mốc phát hiện sự cố (UTC) — AI so khoảng cách thời gian giữa hai ticket.</summary>
    public DateTime? DetectedAt { get; set; }

    /// <summary>
    /// Mô tả do MÁY sinh từ template cố định (<c>AutoFromAlert</c> / <c>System</c>).
    ///
    /// <para>AI chọn thuật toán dò trùng theo cờ này, vì hai loại mô tả hỏng theo hai kiểu ngược
    /// nhau: mô tả máy dùng chung template nên đếm-từ-chung cho điểm cao giả tạo giữa các lỗi
    /// khác hẳn nhau, còn mô tả người viết thì cùng một sự cố lại gần như không chung từ nào.</para>
    /// </summary>
    public bool IsMachineWritten { get; set; }
}

/// <summary>
/// Kết quả AI verify ticket (chấm điểm thật/rác + dò trùng). Null khi client fail (verify skip).
/// </summary>
public class AiVerifyResult
{
    /// <summary>"legitimate" | "suspicious".</summary>
    public string Verdict { get; set; } = string.Empty;

    /// <summary>[0..1] độ hợp lệ (1 = chắc chắn thật).</summary>
    public double Score { get; set; }

    /// <summary>Lý do verdict (tiếng Việt, cho Manager đọc).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Id ticket bị nghi trùng ("" nếu không nghi).</summary>
    public string? DuplicateOfTicketId { get; set; }

    /// <summary>[0..1] độ tương đồng cao nhất với candidate.</summary>
    public double DuplicateScore { get; set; }

    /// <summary>Lý do nghi trùng.</summary>
    public string? DuplicateReason { get; set; }
}
