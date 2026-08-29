using TicketService.Application.DTOs.Response.Chats;
using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Tickets;

public class TicketChatDTO
{
    /// <summary>
    /// Id.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    public string TicketId { get; set; } = string.Empty;
    public string AuthorUserId { get; set; } = string.Empty;
    /// <summary>
    /// Vai trò của tác giả.
    /// </summary>
    public ActorRoleEnum AuthorRole { get; set; }
    public string? AuthorDisplayName { get; set; }
    public string Body { get; set; } = string.Empty;
    /// <summary>
    /// Xác định bình luận/hoạt động này có phải là nội bộ (chỉ Staff/Manager xem được) hay không.
    /// </summary>
    public bool IsInternal { get; set; }
    public List<string> AttachmentFileIds { get; set; } = new();
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Edited at.
    /// </summary>
    public DateTime? EditedAt { get; set; }
    public int EditCount { get; set; }
    public string? LastEditedByUserId { get; set; }
    /// <summary>
    /// Body format.
    /// </summary>
    public ChatBodyFormatEnum BodyFormat { get; set; }
    public string? BodyHtml { get; set; }
    public string? ParentChatId { get; set; }
    /// <summary>
    /// Thread root id.
    /// </summary>
    public string? ThreadRootId { get; set; }
    public int ReplyCount { get; set; }
    public bool IsPinned { get; set; }
    /// <summary>
    /// Pinned at.
    /// </summary>
    public DateTime? PinnedAt { get; set; }
    public string? PinnedByUserId { get; set; }

    /// <summary>Chi tiết attachment đầy đủ — chỉ điền khi GetById (#509); GetList vẫn dùng AttachmentFileIds.</summary>
    public List<TicketAttachmentDTO>? Attachments { get; set; }

    public List<TicketChatMentionDTO> Mentions { get; set; } = new();
    /// <summary>
    /// Reactions.
    /// </summary>
    public TicketChatReactionsAggregateDTO Reactions { get; set; } = new();

    /// <summary>Bản dịch user hiện tại đã yêu cầu — null nếu chưa dịch.</summary>
    public ChatTranslateDTO? ActiveTranslation { get; set; }

    /// <summary>
    /// User hiện tại đã đọc tin này chưa (theo bảng TicketChatRead).
    /// Tin do CHÍNH MÌNH gửi luôn là true — không ai phải "đọc" tin của chính mình.
    /// Client dùng để vẽ mốc "Tin nhắn chưa đọc" và cuộn tới tin cũ nhất chưa đọc.
    /// </summary>
    public bool IsRead { get; set; }

    /// <summary>
    /// Những người KHÁC đã đọc tin này (không gồm chính tác giả) — kiểu "đã xem" của Messenger.
    /// Client vẽ avatar chồng dưới bubble tin cuối cùng mà mỗi người đã đọc.
    ///
    /// KHÁC <see cref="IsRead"/>: IsRead = "TÔI đã đọc tin này" (vẽ mốc "Tin nhắn chưa đọc"),
    /// còn danh sách này = "AI đã đọc tin này". Tin mình gửi luôn có IsRead=true nhưng
    /// ReadReceipts rỗng cho tới khi có người thật sự mở đọc.
    ///
    /// Chỉ điền cho tin do CHÍNH actor gửi — tin của người khác không cần biết ai đã xem,
    /// điền hết sẽ phình payload mà client không dùng tới.
    /// </summary>
    public List<ChatReaderDTO> ReadReceipts { get; set; } = new();

    /// <summary>Số người đã đọc — bằng <c>ReadReceipts.Count</c>, tách ra để client hiển thị nhanh.</summary>
    public int ReadCount { get; set; }

    public bool IsDeleted { get; set; }
    public VoiceTranscriptionStatusEnum? VoiceTranscriptionStatus { get; set; }
    public string? VoiceTranscriptionError { get; set; }
    public DateTime? TranscribedAt { get; set; }
}
