using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Chats;

public class ChatBulkDeleteCommand : IRequest<ChatBulkDeleteResponse>, IValidatable<ChatBulkDeleteResponse>
{
    [JsonIgnore]
    public Guid TicketId { get; set; }
    [JsonIgnore]
    public Guid UserId { get; set; }
    [JsonIgnore]
    public ActorRoleEnum UserRole { get; set; }
    [JsonIgnore]
    public string UserDisplayName { get; set; } = string.Empty;
    [JsonIgnore]
    public List<string> UserPermissions { get; set; } = new();

    public List<Guid> ChatIds { get; set; } = new();

    private const int MaxBatchSize = 50;

    public Task<ChatBulkDeleteResponse> ValidateAsync()
    {
        var response = new ChatBulkDeleteResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "Invalid TicketId." });

        if (UserId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "Invalid UserId." });

        if (ChatIds == null || ChatIds.Count == 0)
            response.ListErrors.Add(new Errors { Field = "ChatIds", Detail = "ChatIds list must not be empty." });
        else if (ChatIds.Count > MaxBatchSize)
            response.ListErrors.Add(new Errors { Field = "ChatIds", Detail = $"A maximum of {MaxBatchSize} chats can be deleted at once." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
