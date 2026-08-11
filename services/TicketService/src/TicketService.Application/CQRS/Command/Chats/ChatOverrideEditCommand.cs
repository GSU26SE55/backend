using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Chats;

/// <summary>
/// Admin override — sửa bình luận dù ticket đang <c>Closed</c>/<c>ClosedPendingRate</c> (#517).
/// Endpoint riêng biệt, bắt buộc <see cref="OverrideReason"/>, chỉ Admin gọi được.
/// </summary>
public class ChatOverrideEditCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    [JsonIgnore]
    public Guid TicketId { get; set; }
    /// <summary>
    /// ID của Chat/Bình luận.
    /// </summary>
    [JsonIgnore]
    public Guid ChatId { get; set; }
    [JsonIgnore]
    public Guid UserId { get; set; }
    /// <summary>
    /// Vai trò của người thực hiện.
    /// </summary>
    [JsonIgnore]
    public ActorRoleEnum UserRole { get; set; }
    [JsonIgnore]
    public string UserDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Nội dung chi tiết.
    /// </summary>
    public required string Body { get; set; }
    public required string OverrideReason { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "Invalid TicketId." });

        if (ChatId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "ChatId", Detail = "Invalid ChatId." });

        if (UserId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "Invalid UserId." });

        if (string.IsNullOrWhiteSpace(Body))
            response.ListErrors.Add(new Errors { Field = "Body", Detail = "Comment content is required." });
        else if (Body.Length > 10000)
            response.ListErrors.Add(new Errors { Field = "Body", Detail = "Comment content must be at most 10000 characters." });

        if (string.IsNullOrWhiteSpace(OverrideReason))
            response.ListErrors.Add(new Errors { Field = "OverrideReason", Detail = "An override reason is required when the ticket is closed." });
        else if (OverrideReason.Length > 1000)
            response.ListErrors.Add(new Errors { Field = "OverrideReason", Detail = "Override reason must be at most 1000 characters." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
