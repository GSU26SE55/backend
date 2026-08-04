using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Query.Chats;

public class ChatFileSummaryQuery : IRequest<CommonResponse<List<TicketAttachmentDTO>>>
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
}
