using System;
using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;

namespace TicketService.Application.CQRS.Command.ChatTemplates;

public class ChatTemplateDeleteCommand : IRequest<CommonResponse<object>>
{
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
    /// Danh sách vai trò của người thực hiện.
    /// </summary>
    [JsonIgnore]
    public string[] ActorRoles { get; set; } = Array.Empty<string>();
}
