using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Chats;

public class ChatMarkAsReadCommand : IRequest<ChatMarkAsReadResponse>, IValidatable<ChatMarkAsReadResponse>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; set; }
    [JsonIgnore]
    public Guid UserId { get; set; }
    /// <summary>
    /// Vai trò của người thực hiện.
    /// </summary>
    [JsonIgnore]
    public ActorRoleEnum UserRole { get; set; }
    [JsonIgnore]
    public string[] ActorRoles { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Chat ids.
    /// </summary>
    public List<Guid> ChatIds { get; set; } = new();

    public Task<ChatMarkAsReadResponse> ValidateAsync()
    {
        var response = new ChatMarkAsReadResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (UserId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "UserId không hợp lệ." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
