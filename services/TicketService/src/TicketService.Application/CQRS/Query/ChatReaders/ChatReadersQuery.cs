using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Query.ChatReaders;

public class ChatReadersQuery : IRequest<ChatReadersResponse>
{
    [JsonIgnore]
    [BindNever]
    public Guid TicketId { get; set; }
    [JsonIgnore]
    [BindNever]
    public Guid ChatId { get; set; }
    [JsonIgnore]
    [BindNever]
    public Guid ActorUserId { get; set; }
    [JsonIgnore]
    [BindNever]
    public string[] ActorRoles { get; set; } = Array.Empty<string>();
}
