using System.Text.Json.Serialization;
using MediatR;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.ChatSuggest;

public class ChatSuggestCommand : IRequest<ChatSuggestResponse>
{
    [JsonIgnore]
    public Guid TicketId { get; set; }

    [JsonIgnore]
    public Guid CurrentUserId { get; set; }

    /// <summary>Phong cách gợi ý — Staff chọn trước khi click "AI Suggest".</summary>
    public ChatAiIntentEnum Intent { get; set; } = ChatAiIntentEnum.TechnicalAnswer;
}
