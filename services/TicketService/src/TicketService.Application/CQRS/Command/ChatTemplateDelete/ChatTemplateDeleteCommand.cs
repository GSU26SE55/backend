using System;
using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;

namespace TicketService.Application.CQRS.Command.ChatTemplateDelete;

public class ChatTemplateDeleteCommand : IRequest<CommonResponse<object>>
{
    [JsonIgnore]
    public Guid TemplateId { get; set; }

    [JsonIgnore]
    public Guid ActorUserId { get; set; }

    [JsonIgnore]
    public string[] ActorRoles { get; set; } = Array.Empty<string>();
}
