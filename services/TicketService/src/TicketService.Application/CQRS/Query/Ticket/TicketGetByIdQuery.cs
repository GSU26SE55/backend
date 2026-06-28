using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Query.Ticket;

public class TicketGetByIdQuery : IRequest<CommonResponse<TicketDetailDTO>>
{
    /// <summary>
    /// Id.
    /// </summary>
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }
    /// <summary>
    /// ID của người thực hiện yêu cầu.
    /// </summary>
    public Guid? ActorUserId { get; set; }
    public IReadOnlyCollection<string> ActorRoles { get; set; } = Array.Empty<string>();
}
