using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Chats;

public class ChatDeleteCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
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
    /// Tên hiển thị của người thực hiện.
    /// </summary>
    [JsonIgnore]
    public string UserDisplayName { get; set; } = string.Empty;
    [JsonIgnore]
    public List<string> UserPermissions { get; set; } = new();

    /// <summary>
    /// Lý do xóa.
    /// </summary>
    public string? DeleteReason { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (ChatId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "ChatId", Detail = "ChatId không hợp lệ." });

        if (UserId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "UserId không hợp lệ." });

        if (!string.IsNullOrEmpty(DeleteReason) && DeleteReason.Length > 1000)
            response.ListErrors.Add(new Errors { Field = "DeleteReason", Detail = "Lý do xóa tối đa 1000 ký tự." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
