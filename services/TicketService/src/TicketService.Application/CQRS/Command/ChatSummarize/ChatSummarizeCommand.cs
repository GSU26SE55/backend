using System.Text.Json.Serialization;
using MediatR;
using TicketService.Application.DTOs.Response.Chats;

namespace TicketService.Application.CQRS.Command.ChatSummarize;

public class ChatSummarizeCommand : IRequest<ChatSummarizeResponse>
{
    [JsonIgnore]
    public Guid TicketId { get; set; }

    [JsonIgnore]
    public Guid CurrentUserId { get; set; }
}
