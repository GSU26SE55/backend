using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.ChatAttachKbReference;

public class ChatAttachKbReferenceCommand : IRequest<CommonResponse<object>>, IValidatable<CommonResponse<object>>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; set; }
    [JsonIgnore]
    public Guid ChatId { get; set; }
    /// <summary>
    /// ID của người dùng hiện tại thực hiện hành động.
    /// </summary>
    [JsonIgnore]
    public Guid CurrentUserId { get; set; }

    /// <summary>
    /// ID của bài viết Knowledge Base.
    /// </summary>
    public Guid KbArticleId { get; set; }
    public KbReferenceTypeEnum ReferenceType { get; set; }
    public string? Note { get; set; }

    public Task<CommonResponse<object>> ValidateAsync()
    {
        var response = new CommonResponse<object>();

        if (KbArticleId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "KbArticleId", Detail = "KbArticleId không hợp lệ." });

        if (!Enum.IsDefined(typeof(KbReferenceTypeEnum), ReferenceType))
            response.ListErrors.Add(new Errors { Field = "ReferenceType", Detail = "Loại tham chiếu không hợp lệ." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
