using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;

namespace TicketService.Application.CQRS.Query.ChatAttachmentDownload;

public class ChatAttachmentDownloadQuery : IRequest<CommonResponse<string>>
{
    [JsonIgnore]
    [BindNever]
    public Guid TicketId { get; set; }

    [JsonIgnore]
    [BindNever]
    public Guid ChatId { get; set; }

    [JsonIgnore]
    [BindNever]
    public Guid AttachmentId { get; set; }

    [JsonIgnore]
    [BindNever]
    public Guid ActorUserId { get; set; }

    [JsonIgnore]
    [BindNever]
    public string[] ActorRoles { get; set; } = Array.Empty<string>();
}
