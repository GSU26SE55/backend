namespace BatteryService.Application.Common.Models;

/// <summary>
/// BE-AI — kết quả <c>Health</c> (gRPC) / <c>GET /health</c> (HTTP) đã map về domain BE.
/// </summary>
/// <remarks>
/// Trước đây BE KHÔNG hề gọi Health. Hệ quả cụ thể, không phải giả định:
/// <list type="bullet">
/// <item>
/// Không biết AI đã nạp bộ trọng số LFP chưa. 6/10 asset trong <c>battery_db</c> là
/// LiFePO4 ⇒ mọi request của chúng sẽ fail ở tầng inference thay vì được chặn sớm.
/// </item>
/// <item>
/// Không biết <see cref="SocMode"/> — thứ quyết định BE phải gửi <c>soc_percent</c>
/// kiểu nào. Gửi sai KHÔNG bị AI từ chối; nó chỉ lặng lẽ dịch SOH đi (đo được ~27 điểm).
/// </item>
/// </list>
/// </remarks>
public class AiHealthResult
{
    public AiHealthResult(
        string Status,
        string ModelVersion,
        bool ScalerLoaded,
        bool MambaLoaded,
        bool IsolationForestLoaded,
        bool LfpLoaded,
        string LfpModelVersion,
        string SocMode,
        string LfpSocMode,
        bool LongLoaded,
        string LongModelVersion)
    {
        this.Status = Status;
        this.ModelVersion = ModelVersion;
        this.ScalerLoaded = ScalerLoaded;
        this.MambaLoaded = MambaLoaded;
        this.IsolationForestLoaded = IsolationForestLoaded;
        this.LfpLoaded = LfpLoaded;
        this.LfpModelVersion = LfpModelVersion;
        this.SocMode = SocMode;
        this.LfpSocMode = LfpSocMode;
        this.LongLoaded = LongLoaded;
        this.LongModelVersion = LongModelVersion;
    }

    public string Status { get; }
    public string ModelVersion { get; }
    public bool ScalerLoaded { get; }
    public bool MambaLoaded { get; }
    public bool IsolationForestLoaded { get; }

    /// <summary>
    /// Bộ artifact LFP (train trên Severson) đã nạp chưa. Deploy chỉ-NASA vẫn boot bình
    /// thường, nhưng khi đó MỌI request mang <c>chemistry="LFP"</c> sẽ fail ở inference.
    /// </summary>
    public bool LfpLoaded { get; }

    public string LfpModelVersion { get; }

    /// <summary>
    /// Định nghĩa <c>soc_percent</c> mà bộ MẶC ĐỊNH (NASA/NMC) được train:
    /// <c>"window"</c> | <c>"cycle"</c> | <c>"unknown"</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ Đây là thuộc tính của BỘ ARTIFACT, KHÔNG phải của chemistry. Suy ra từ chemistry
    /// sẽ hỏng âm thầm đúng vào ngày một bộ được retrain với định nghĩa kia — mà
    /// <c>soc_percent</c> sai thì AI không bao giờ ném lỗi, nó chỉ trả SOH khác đi.
    /// Vì vậy BE PHẢI đọc field này thay vì hardcode.
    /// </remarks>
    public string SocMode { get; }

    /// <summary>
    /// Như <see cref="SocMode"/> nhưng cho bộ LFP. <c>""</c> khi <see cref="LfpLoaded"/> = false.
    /// </summary>
    public string LfpSocMode { get; }

    /// <summary>
    /// Model long-sequence (<c>PredictLong</c>) đã nạp chưa.
    /// ⚠️ <c>false</c> KHÔNG phải lỗi — model này nạp LƯỜI ở lần gọi đầu tiên, nên
    /// <c>false</c> chỉ nghĩa là "chưa ai gọi".
    /// </summary>
    public bool LongLoaded { get; }

    public string LongModelVersion { get; }

    /// <summary>
    /// <c>soc_mode</c> áp dụng cho một chemistry cụ thể — dùng để quyết định gửi 4 hay 6 cột.
    /// </summary>
    /// <remarks>
    /// Trả <c>"unknown"</c> khi bộ artifact tương ứng chưa nạp (LFP chưa có ⇒ <c>LfpSocMode</c>
    /// rỗng). Caller phải coi <c>"unknown"</c> là "đừng gửi 6 cột" — gửi 4 cột luôn hợp lệ
    /// với <c>soc_mode="window"</c>, còn với <c>"cycle"</c> thì AI từ chối RÕ RÀNG
    /// (INVALID_ARGUMENT) thay vì trả số sai.
    /// </remarks>
    public string SocModeFor(string? chemistry)
    {
        var mode = string.Equals(chemistry, "LFP", StringComparison.OrdinalIgnoreCase)
            ? LfpSocMode
            : SocMode;
        return string.IsNullOrEmpty(mode) ? "unknown" : mode;
    }
}
