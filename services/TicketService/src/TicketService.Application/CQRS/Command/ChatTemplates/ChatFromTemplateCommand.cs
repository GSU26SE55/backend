using System;
using System.Text.Json.Serialization;
using MediatR;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.ChatTemplates;

public class ChatFromTemplateCommand : IRequest<TicketActionResponse>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; set; }

    /// <summary>
    /// ID của mẫu phản hồi/chat.
    /// </summary>
    [JsonIgnore]
    public Guid TemplateId { get; set; }

    /// <summary>
    /// ID của người thực hiện yêu cầu.
    /// </summary>
    [JsonIgnore]
    public Guid ActorUserId { get; set; }

    /// <summary>
    /// Actor role.
    /// </summary>
    [JsonIgnore]
    public ActorRoleEnum ActorRole { get; set; }

    /// <summary>
    /// Actor display name.
    /// </summary>
    [JsonIgnore]
    public string ActorDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Danh sách vai trò của người thực hiện.
    /// </summary>
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
