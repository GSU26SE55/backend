using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Query.MyMentions;

public class MyMentionsQuery : IRequest<MyMentionsResponse>
{
    /// <summary>
    /// ID của người thực hiện yêu cầu.
    /// </summary>
    [JsonIgnore]
    [BindNever]
    public Guid ActorUserId { get; set; }

    /// <summary>
    /// Unread only.
    /// </summary>
    public bool UnreadOnly { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
