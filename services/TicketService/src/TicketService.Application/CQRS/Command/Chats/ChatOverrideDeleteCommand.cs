using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Chats;

/// <summary>
/// Admin override — xóa bình luận dù ticket đang <c>Closed</c>/<c>ClosedPendingRate</c> (#517).
/// Endpoint riêng biệt, bắt buộc <see cref="OverrideReason"/>, chỉ Admin gọi được.
/// </summary>
public class ChatOverrideDeleteCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
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
    /// Override reason.
    /// </summary>
    public required string OverrideReason { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (ChatId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "ChatId", Detail = "ChatId không hợp lệ." });

        if (UserId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "UserId không hợp lệ." });

        if (string.IsNullOrWhiteSpace(OverrideReason))
            response.ListErrors.Add(new Errors { Field = "OverrideReason", Detail = "Bắt buộc nhập lý do override khi ticket đã đóng." });
        else if (OverrideReason.Length > 1000)
            response.ListErrors.Add(new Errors { Field = "OverrideReason", Detail = "Lý do override tối đa 1000 ký tự." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
