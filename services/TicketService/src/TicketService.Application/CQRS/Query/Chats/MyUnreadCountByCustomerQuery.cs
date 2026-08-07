using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Query.Chats;

/// <summary>
/// Số tin nhắn chưa đọc của actor, gom theo từng Customer.
/// Dùng cho màn "Khách hàng" của Staff — 1 call thay vì N call per-ticket.
/// </summary>
public class MyUnreadCountByCustomerQuery : IRequest<UnreadCountByCustomerResponse>
{
    [JsonIgnore]
    [BindNever]
    public Guid ActorUserId { get; set; }

    [JsonIgnore]
    [BindNever]
    public string[] ActorRoles { get; set; } = Array.Empty<string>();
}
