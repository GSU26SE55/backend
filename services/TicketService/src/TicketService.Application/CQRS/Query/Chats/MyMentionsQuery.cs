using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Query.Chats;

public class MyMentionsQuery : IRequest<MyMentionsResponse>
{
    /// <summary>
    /// ID của người thực hiện yêu cầu.
    /// </summary>
    [BindNever]
    public Guid ActorUserId { get; set; }

    [BindNever]
    public List<string> ActorRoles { get; set; } = new();

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
