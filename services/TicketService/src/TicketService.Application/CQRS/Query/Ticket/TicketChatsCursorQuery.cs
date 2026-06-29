using System;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Query.Ticket;

public class TicketChatsCursorQuery : IRequest<CommonResponse<CursorPaginationResponse<TicketChatDTO>>>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    [JsonIgnore]
    [BindNever]
    public Guid TicketId { get; set; }

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

    /// <summary>
    /// Opaque cursor: base64("{chatId}:{createdAtTicks}") từ NextCursor của trang trước.
    /// </summary>
    public string? Cursor { get; set; }

    public int Limit { get; set; } = 20;
}
