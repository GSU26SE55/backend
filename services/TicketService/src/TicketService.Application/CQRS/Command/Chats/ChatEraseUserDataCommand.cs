using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;

namespace TicketService.Application.CQRS.Command.Chats;

/// <summary>
/// GDPR right-to-erasure: bulk-redact all chats authored by the requesting user.
/// Body replaced with "[REDACTED — GDPR erasure]", IsRedacted=true, audit trail preserved. #569.
/// </summary>
public class ChatEraseUserDataCommand : IRequest<CommonResponse<object>>
{
    [JsonIgnore] public Guid RequestingUserId { get; set; }
}
