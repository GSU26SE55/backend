using System;
using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.ChatTemplateUpdate;

public class ChatTemplateUpdateCommand : IRequest<CommonResponse<ChatTemplateDTO>>
{
    [JsonIgnore]
    public Guid TemplateId { get; set; }

    [JsonIgnore]
    public Guid ActorUserId { get; set; }

    [JsonIgnore]
    public string[] ActorRoles { get; set; } = Array.Empty<string>();

    public string? Name { get; set; }
    public string? Content { get; set; }
    public ChatTemplateCategoryEnum? Category { get; set; }
    public bool? IsInternalDefault { get; set; }
    public bool? IsActive { get; set; }
}
