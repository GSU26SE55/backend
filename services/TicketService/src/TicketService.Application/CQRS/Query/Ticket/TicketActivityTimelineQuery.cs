using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Ticket;

namespace TicketService.Application.CQRS.Query.Ticket;

public class TicketActivityTimelineQuery : IRequest<CommonResponse<List<TicketActivityDTO>>>
{
    [JsonIgnore]
    [BindNever]
    public Guid TicketId { get; set; }

    [JsonIgnore]
    [BindNever]
    public Guid? ActorUserId { get; set; }

    [JsonIgnore]
    [BindNever]
    public IReadOnlyCollection<string> ActorRoles { get; set; } = Array.Empty<string>();
}
