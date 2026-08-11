using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Chats;

public class ChatReactionAddCommand : IRequest<ChatReactionActionResponse>, IValidatable<ChatReactionActionResponse>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; set; }
    [JsonIgnore]
    public Guid ChatId { get; set; }
    /// <summary>
    /// ID của người dùng.
    /// </summary>
    [JsonIgnore]
    public Guid UserId { get; set; }
    [JsonIgnore]
    public ActorRoleEnum UserRole { get; set; }
    /// <summary>
    /// Danh sách vai trò của người thực hiện.
    /// </summary>
    [JsonIgnore]
    public string[] ActorRoles { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Reaction type.
    /// </summary>
    public ReactionTypeEnum ReactionType { get; set; }

    public Task<ChatReactionActionResponse> ValidateAsync()
    {
        var response = new ChatReactionActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "Invalid TicketId." });

        if (ChatId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "ChatId", Detail = "Invalid ChatId." });

        if (UserId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "Invalid UserId." });

        if (!Enum.IsDefined(typeof(ReactionTypeEnum), ReactionType))
            response.ListErrors.Add(new Errors { Field = "ReactionType", Detail = "Invalid ReactionType." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
