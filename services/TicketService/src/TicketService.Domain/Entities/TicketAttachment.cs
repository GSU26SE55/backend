using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class TicketAttachment : AuditableEntity
{
    public Guid TicketId { get; set; }
    public Guid? SourceTicketId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public Guid FileId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public AttachmentSourceEnum Source { get; set; }
    public Guid? ChatId { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Url { get; set; }
    public bool IsInline { get; set; }
    public int DownloadCount { get; set; }
    public VirusScanStatusEnum VirusScanStatus { get; set; } = VirusScanStatusEnum.Pending;

    /// <summary>
    /// GH-790 — số lần đã thử quét nhưng hỏng (tải file lỗi, ClamAV không trả lời…).
    /// </summary>
    /// <remarks>
    /// Trước đây một lần hỏng là ghi thẳng <see cref="VirusScanStatusEnum.Failed"/>, mà worker chỉ
    /// quét bản ghi <see cref="VirusScanStatusEnum.Pending"/> ⇒ không bao giờ thử lại. Giữ bộ đếm để
    /// phân biệt "hỏng tạm thời, thử lại" với "hỏng hẳn, cần người xem".
    /// </remarks>
    public int VirusScanAttempts { get; set; }

    /// <summary>
    /// GH-790 — UTC, thời điểm bắt đầu lượt quét gần nhất.
    /// </summary>
    /// <remarks>
    /// Dùng cho hai việc: giãn nhịp thử lại (backoff), và thu hồi bản ghi kẹt ở
    /// <see cref="VirusScanStatusEnum.Scanning"/> vì tiến trình chết giữa chừng. Không có mốc này
    /// thì hoặc kẹt vĩnh viễn, hoặc phải thu hồi mù và quét trùng.
    /// </remarks>
    public DateTime? VirusScanLastAttemptAt { get; set; }

    public required Ticket Ticket { get; set; }
    public TicketChat? Chat { get; set; }
}
