using System;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Query.ChatGlobalSearch;

public class ChatGlobalSearchQuery : IRequest<CommonResponse<PaginationResponse<TicketChatDTO>>>
{
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
    /// Từ khóa tìm kiếm.
    /// </summary>
    public string? Q { get; set; }
    public Guid? CustomerId { get; set; }
    public DateTime? DateFrom { get; set; }
    /// <summary>
    /// Lọc đến ngày (UTC).
    /// </summary>
    public DateTime? DateTo { get; set; }
    public ActorRoleEnum? AuthorRole { get; set; }
    public bool? IsInternal { get; set; }

    /// <summary>
    /// Số trang hiện tại (bắt đầu từ 1).
    /// </summary>
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
