using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Command.ParticipantSelfLeave;

public class ParticipantSelfLeaveCommand : IRequest<ParticipantActionResponse>, IValidatable<ParticipantActionResponse>
{
    [JsonIgnore]
    public Guid TicketId { get; set; }

    public string? LeaveReason { get; set; }

    [JsonIgnore]
    public Guid ActorUserId { get; set; }

    public Task<ParticipantActionResponse> ValidateAsync()
    {
        var response = new ParticipantActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
