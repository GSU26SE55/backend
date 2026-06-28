using TicketService.Application.DTOs.Response.Chats;
using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Tickets;

public class TicketChatDTO
{
    public string Id { get; set; } = string.Empty;
    public string TicketId { get; set; } = string.Empty;
    public string AuthorUserId { get; set; } = string.Empty;
    public ActorRoleEnum AuthorRole { get; set; }
    public string? AuthorDisplayName { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsInternal { get; set; }
    public List<string> AttachmentFileIds { get; set; } = new();
    public DateTime CreatedAt { get; set; }

    public DateTime? EditedAt { get; set; }
    public int EditCount { get; set; }
    public string? LastEditedByUserId { get; set; }
    public ChatBodyFormatEnum BodyFormat { get; set; }
    public string? BodyHtml { get; set; }
    public string? ParentChatId { get; set; }
    public string? ThreadRootId { get; set; }
    public int ReplyCount { get; set; }
    public bool IsPinned { get; set; }
    public DateTime? PinnedAt { get; set; }
    public string? PinnedByUserId { get; set; }

    /// <summary>Chi tiết attachment đầy đủ — chỉ điền khi GetById (#509); GetList vẫn dùng AttachmentFileIds.</summary>
    public List<TicketAttachmentDTO>? Attachments { get; set; }

    public List<TicketChatMentionDTO> Mentions { get; set; } = new();
    public TicketChatReactionsAggregateDTO Reactions { get; set; } = new();

    /// <summary>Bản dịch user hiện tại đã yêu cầu — null nếu chưa dịch.</summary>
    public ChatTranslateDTO? ActiveTranslation { get; set; }
}
