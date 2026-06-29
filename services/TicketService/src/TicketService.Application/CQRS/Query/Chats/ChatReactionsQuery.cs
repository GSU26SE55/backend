using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Chats;

namespace TicketService.Application.CQRS.Query.Chats;

public class ChatReactionsQuery : IRequest<CommonResponse<TicketChatReactionsAggregateDTO>>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    [JsonIgnore]
    [BindNever]
    public Guid TicketId { get; set; }
    /// <summary>
    /// ID của Chat/Bình luận.
    /// </summary>
    [JsonIgnore]
    [BindNever]
    public Guid ChatId { get; set; }
    /// <summary>
    /// ID của người thực hiện yêu cầu.
    /// </summary>
    [JsonIgnore]
    [BindNever]
    public Guid ActorUserId { get; set; }
    /// <summary>
    /// Danh sách vai trò của người thực hiện.
    /// </summary>
    [JsonIgnore]
    [BindNever]
    public string[] ActorRoles { get; set; } = Array.Empty<string>();
}
