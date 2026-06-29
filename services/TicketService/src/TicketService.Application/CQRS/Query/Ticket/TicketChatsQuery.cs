using System;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Query.Ticket;

public class TicketChatsQuery : IRequest<CommonResponse<PaginationResponse<TicketChatDTO>>>
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
    /// Số trang hiện tại (bắt đầu từ 1).
    /// </summary>
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;

    // #549 — Extended filters
    /// <summary>
    /// Search.
    /// </summary>
    public string? Search { get; set; }
    public Guid? AuthorUserId { get; set; }
    public ActorRoleEnum? AuthorRole { get; set; }
    /// <summary>
    /// Xác định bình luận/hoạt động này có phải là nội bộ (chỉ Staff/Manager xem được) hay không.
    /// </summary>
    public bool? IsInternal { get; set; }
    public bool? IsPinned { get; set; }
    public bool? HasAttachments { get; set; }
    /// <summary>
    /// Mentioned me.
    /// </summary>
    public bool? MentionedMe { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
}
