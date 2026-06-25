using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Chats;

namespace TicketService.Application.CQRS.Query.ChatReactions;

public class ChatReactionsQuery : IRequest<CommonResponse<TicketChatReactionsAggregateDTO>>
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
