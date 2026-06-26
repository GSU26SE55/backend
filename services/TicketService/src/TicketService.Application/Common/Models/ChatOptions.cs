namespace TicketService.Application.Common.Models;

public class ChatOptions
{
    public const string SectionName = "Chat";

    /// <summary>
    /// Default cho <see cref="MaxBodyLength"/> — đồng thời dùng làm hằng số tham chiếu trong
    /// <c>ChatAddCommand.ValidateAsync()</c> (không inject được <see cref="ChatOptions"/> tại đó
    /// vì <c>IValidatable&lt;T&gt;.ValidateAsync()</c> không nhận DI) để tránh lặp số tay 2 nơi.
    /// </summary>
    public const int MaxBodyLengthDefault = 10000;

    public int EditWindowMinutes { get; set; } = 15;
    public int MinBodyLength { get; set; } = 1;
    public int MaxBodyLength { get; set; } = MaxBodyLengthDefault;

    /// <summary>
    /// Block create/edit/delete chat khi ticket ở trạng thái Closed.
    /// Admin có thể override khi true nhưng bắt buộc kèm OverrideReason.
    /// Default: true.
    /// </summary>
    public bool BlockEditOnClosed { get; set; } = true;

    public int MaxAttachmentsPerChat { get; set; } = 10;
    public long MaxAttachmentSizeBytes { get; set; } = 52428800; // 50MB
    public List<string> AllowedAttachmentMimeTypes { get; set; } = new()
    {
        "image/*", "application/pdf", "video/mp4", "text/plain"
    };

    /// <summary>
    /// Từ điển profanity theo ngôn ngữ — key "VN"/"EN" (case-insensitive khi load).
    /// Dùng bởi <c>IProfanityFilter</c> — chỉ cảnh báo, không block (#519).
    /// </summary>
    public Dictionary<string, List<string>> ProfanityWords { get; set; } = new();

    /// <summary>Feature toggles (#514).</summary>
    public FeaturesSection Features { get; set; } = new();

    /// <summary>ClamAV virus scan config (#514).</summary>
    public VirusScanSection VirusScan { get; set; } = new();

    /// <summary>Cấu hình AI suggest endpoint (#559).</summary>
    public AiSection Ai { get; set; } = new();

    public class FeaturesSection
    {
        /// <summary>Master switch — mặc định false (disabled) cho đến khi ClamAV deploy xong.</summary>
        public bool EnableVirusScan { get; set; } = false;
    }

    public class VirusScanSection
    {
        /// <summary>ClamAV REST base URL, e.g. http://clamav:3000</summary>
        public string Endpoint { get; set; } = "http://clamav:3000";

        public int TimeoutSeconds { get; set; } = 60;

        /// <summary>Số attachment Pending xử lý mỗi lần poll.</summary>
        public int BatchSize { get; set; } = 20;

        /// <summary>Khoảng cách giữa 2 lần poll (giây).</summary>
        public int IntervalSeconds { get; set; } = 30;

        /// <summary>Base URL của FileStorageService để download file trước khi scan.</summary>
        public string FileStorageBaseUrl { get; set; } = "http://file-storage-service";
    }

    public class AiSection
    {
        /// <summary>Gemini (hoặc LLM khác) API key — điền vào appsettings.Development.json khi test.</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>Full URL tới generateContent endpoint (không gồm query ?key=...).</summary>
        public string SuggestModelEndpoint { get; set; } =
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash-lite:generateContent";

        public int MaxSuggestionsPerCall { get; set; } = 3;

        /// <summary>Timeout gọi LLM API (giây).</summary>
        public int TimeoutSeconds { get; set; } = 15;

        /// <summary>TTL cache mask map PII trong Redis (giờ).</summary>
        public int PiiMaskTtlHours { get; set; } = 1;

        /// <summary>Ngưỡng sentiment score để alert Manager (#560). Âm = tiêu cực; default -0.7.</summary>
        public double SentimentAlertThreshold { get; set; } = -0.7;

        /// <summary>Số dòng tóm tắt cho summarize endpoint (#560).</summary>
        public int SummarizeLinesCount { get; set; } = 5;

        /// <summary>Số chat Customer gần nhất để phân tích sentiment (#560).</summary>
        public int SentimentAnalysisMaxChats { get; set; } = 20;
    }
}
