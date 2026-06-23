namespace TicketService.Application.Common.Models;

public class ChatOptions
{
    public const string SectionName = "Chat";

    public int EditWindowMinutes { get; set; } = 15;
    public int MinBodyLength { get; set; } = 1;
    public int MaxBodyLength { get; set; } = 10000;

    public int MaxAttachmentsPerChat { get; set; } = 10;
    public long MaxAttachmentSizeBytes { get; set; } = 52428800; // 50MB
    public List<string> AllowedAttachmentMimeTypes { get; set; } = new()
    {
        "image/*", "application/pdf", "video/mp4", "text/plain"
    };
}
