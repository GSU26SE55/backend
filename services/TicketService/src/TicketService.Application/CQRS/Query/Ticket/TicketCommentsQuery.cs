using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Ticket;

namespace TicketService.Application.CQRS.Query.Ticket;

public class TicketCommentsQuery : IRequest<CommonResponse<PaginationResponse<TicketCommentDTO>>>
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
}
