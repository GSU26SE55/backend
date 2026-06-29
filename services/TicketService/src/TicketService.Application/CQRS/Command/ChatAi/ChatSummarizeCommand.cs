using System.Text.Json.Serialization;
using MediatR;
using TicketService.Application.DTOs.Response.Chats;

namespace TicketService.Application.CQRS.Command.ChatAi;

public class ChatSummarizeCommand : IRequest<ChatSummarizeResponse>
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
}
