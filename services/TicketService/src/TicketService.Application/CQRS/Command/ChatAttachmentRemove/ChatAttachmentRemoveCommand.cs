using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.ChatAttachmentRemove;

public class ChatAttachmentRemoveCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; set; }
    [JsonIgnore]
    public Guid ChatId { get; set; }
    /// <summary>
    /// ID của file đính kèm.
    /// </summary>
    [JsonIgnore]
    public Guid AttachmentId { get; set; }
    [JsonIgnore]
    public Guid UserId { get; set; }
    /// <summary>
    /// Vai trò của người thực hiện.
    /// </summary>
    [JsonIgnore]
    public ActorRoleEnum UserRole { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });
        if (ChatId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "ChatId", Detail = "ChatId không hợp lệ." });
        if (AttachmentId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "AttachmentId", Detail = "AttachmentId không hợp lệ." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
