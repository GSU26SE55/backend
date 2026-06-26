using System;
using System.Text.Json.Serialization;
using MediatR;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.ChatFromTemplate;

public class ChatFromTemplateCommand : IRequest<TicketActionResponse>
{
    [JsonIgnore]
    public Guid TicketId { get; set; }

    [JsonIgnore]
    public Guid TemplateId { get; set; }

    [JsonIgnore]
    public Guid ActorUserId { get; set; }

    [JsonIgnore]
    public ActorRoleEnum ActorRole { get; set; }

    [JsonIgnore]
    public string ActorDisplayName { get; set; } = string.Empty;

    [JsonIgnore]
    public string[] ActorRoles { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional variables để replace placeholder trong template content (key=value).
    /// </summary>
    public Dictionary<string, string>? Variables { get; set; }

    /// <summary>
    /// Override IsInternal của template nếu cần.
    /// </summary>
    public bool? IsInternal { get; set; }
}
