using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Command.ChatMentionAcknowledge;

public class ChatMentionAcknowledgeCommand : IRequest<ChatMentionActionResponse>, IValidatable<ChatMentionActionResponse>
{
    /// <summary>
    /// Mention id.
    /// </summary>
    [JsonIgnore]
    public Guid MentionId { get; set; }
    [JsonIgnore]
    public Guid ActorUserId { get; set; }

    public Task<ChatMentionActionResponse> ValidateAsync()
    {
        var response = new ChatMentionActionResponse();

        if (MentionId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "MentionId", Detail = "MentionId không hợp lệ." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
