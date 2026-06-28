using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Query.MyChats;

public class MyChatsQuery : IRequest<CommonResponse<PaginationResponse<TicketChatDTO>>>
{
    /// <summary>
    /// ID của người thực hiện yêu cầu.
    /// </summary>
    [JsonIgnore]
    [BindNever]
    public Guid ActorUserId { get; set; }

    /// <summary>
    /// Số trang hiện tại (bắt đầu từ 1).
    /// </summary>
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
