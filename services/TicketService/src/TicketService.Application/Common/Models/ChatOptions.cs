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
}
