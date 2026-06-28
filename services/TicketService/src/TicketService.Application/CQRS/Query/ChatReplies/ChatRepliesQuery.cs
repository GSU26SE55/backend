using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Query.ChatReplies;

public class ChatRepliesQuery : IRequest<CommonResponse<PaginationResponse<TicketChatDTO>>>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    [JsonIgnore]
    [BindNever]
    public Guid TicketId { get; set; }

    /// <summary>
    /// ID của bình luận cha nếu đây là bình luận phản hồi (reply).
    /// </summary>
    [JsonIgnore]
    [BindNever]
    public Guid ParentChatId { get; set; }

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
    /// Số trang hiện tại (bắt đầu từ 1).
    /// </summary>
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
