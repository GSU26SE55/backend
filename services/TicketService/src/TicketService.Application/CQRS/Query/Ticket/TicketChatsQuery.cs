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
    [JsonIgnore]
    [BindNever]
    public Guid TicketId { get; set; }

    [JsonIgnore]
    [BindNever]
    public Guid ActorUserId { get; set; }

    [JsonIgnore]
    [BindNever]
    public string[] ActorRoles { get; set; } = Array.Empty<string>();

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;

    // #549 — Extended filters
    public string? Search { get; set; }
    public Guid? AuthorUserId { get; set; }
    public ActorRoleEnum? AuthorRole { get; set; }
    public bool? IsInternal { get; set; }
    public bool? IsPinned { get; set; }
    public bool? HasAttachments { get; set; }
    public bool? MentionedMe { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
}
