using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Chats;

public sealed class ChatVoiceTranscriptionRetryCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    [JsonIgnore] public Guid TicketId { get; set; }
    [JsonIgnore] public Guid ChatId { get; set; }
    [JsonIgnore] public Guid UserId { get; set; }
    [JsonIgnore] public ActorRoleEnum UserRole { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse { IsSuccess = TicketId != Guid.Empty && ChatId != Guid.Empty, StatusCode = 400 };
        if (!response.IsSuccess)
            response.ListErrors.Add(new Errors { Field = "chatId", Detail = "chatId và ticketId là bắt buộc." });
        return Task.FromResult(response);
    }
}
