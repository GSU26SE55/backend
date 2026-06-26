using System.Text.Json.Serialization;
using MediatR;
using TicketService.Application.DTOs.Response.Chats;

namespace TicketService.Application.CQRS.Command.ChatSentimentCheck;

public class ChatSentimentCheckCommand : IRequest<ChatSentimentCheckResponse>
{
    [JsonIgnore]
    public Guid TicketId { get; set; }

    [JsonIgnore]
    public Guid CurrentUserId { get; set; }
}
