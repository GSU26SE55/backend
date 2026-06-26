using System.Text.Json.Serialization;
using MediatR;
using TicketService.Application.DTOs.Response.Chats;

namespace TicketService.Application.CQRS.Command.ChatTranslate;

public class ChatTranslateCommand : IRequest<ChatTranslateResponse>
{
    [JsonIgnore]
    public Guid TicketId { get; set; }

    [JsonIgnore]
    public Guid ChatId { get; set; }

    [JsonIgnore]
    public Guid CurrentUserId { get; set; }

    [JsonIgnore]
    public string TargetLanguage { get; set; } = string.Empty;
}
