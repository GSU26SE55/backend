using System.Text.Json.Serialization;
using MediatR;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.ChatAi;

public class ChatSuggestCommand : IRequest<ChatSuggestResponse>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; set; }

    /// <summary>
    /// ID của người dùng hiện tại thực hiện hành động.
    /// </summary>
    [JsonIgnore]
    public Guid CurrentUserId { get; set; }

    /// <summary>Phong cách gợi ý — Staff chọn trước khi click "AI Suggest".</summary>
    public ChatAiIntentEnum Intent { get; set; } = ChatAiIntentEnum.TechnicalAnswer;
}
