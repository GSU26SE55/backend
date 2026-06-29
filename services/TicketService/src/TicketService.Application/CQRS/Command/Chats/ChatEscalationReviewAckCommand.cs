using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Chats;

/// <summary>
/// Manager ACK escalation review — transition saga Pending → Reviewed. #566.
/// </summary>
public class ChatEscalationReviewAckCommand : IRequest<CommonResponse<object>>
{
    [JsonIgnore] public Guid TicketId { get; set; }
    [JsonIgnore] public Guid ChatId { get; set; }
    [JsonIgnore] public Guid CurrentUserId { get; set; }
    [JsonIgnore] public ActorRoleEnum CurrentUserRole { get; set; }
}
