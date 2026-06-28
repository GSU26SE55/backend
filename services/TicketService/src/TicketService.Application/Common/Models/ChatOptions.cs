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

    /// <summary>
    /// Provider cho AI text/suggestion clients: "Gemini" (default) hoặc "DeepSeek".
    /// Voice transcription luôn dùng Gemini — DeepSeek không hỗ trợ audio.
    /// </summary>
    public string Provider { get; set; } = "Gemini";

    /// <summary>Cấu hình AI suggest endpoint (#559).</summary>
    public AiSection Ai { get; set; } = new();

    /// <summary>Cấu hình DeepSeek Chat Completions API.</summary>
    public DeepSeekSection DeepSeek { get; set; } = new();

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
        /// <summary>Gemini API key.</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>Tên model Gemini — ví dụ: "gemini-2.0-flash-lite", "gemini-2.5-flash". Code tự build URL.</summary>
        public string ModelName { get; set; } = "gemini-2.0-flash-lite";

        /// <summary>Full generateContent endpoint được tính từ ModelName.</summary>
        public string SuggestModelEndpoint =>
            $"https://generativelanguage.googleapis.com/v1beta/models/{ModelName}:generateContent";

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

    public class DeepSeekSection
    {
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>Base URL của DeepSeek API, không trailing slash. Code tự append /v1/chat/completions.</summary>
        public string BaseUrl { get; set; } = "https://api.deepseek.com";

        /// <summary>Model name — ví dụ: "deepseek-chat", "deepseek-v4-flash", "deepseek-reasoner".</summary>
        public string Model { get; set; } = "deepseek-chat";

        public int TimeoutSeconds { get; set; } = 15;

        /// <summary>Full chat completions endpoint được tính từ BaseUrl.</summary>
        public string ChatEndpoint => $"{BaseUrl.TrimEnd('/')}/v1/chat/completions";
    }

    /// <summary>Cấu hình voice-to-text (Gemini multimodal, #567).</summary>
    public VoiceSection Voice { get; set; } = new();

    public class VoiceSection
    {
        public List<string> AllowedAudioMimeTypes { get; set; } = new()
        {
            "audio/mpeg", "audio/wav", "audio/ogg", "audio/webm", "audio/mp4", "audio/flac"
        };

        /// <summary>Tên model Gemini multimodal cho audio — ví dụ: "gemini-2.5-flash".</summary>
        public string ModelName { get; set; } = "gemini-1.5-flash";

        /// <summary>FileStorageService upload URL — dùng để upload audio sau khi transcribe.</summary>
        public string FileStorageUploadUrl { get; set; } = "http://file-storage-service/api/files/upload";

        public int TranscribeTimeoutSeconds { get; set; } = 30;
    }
}
