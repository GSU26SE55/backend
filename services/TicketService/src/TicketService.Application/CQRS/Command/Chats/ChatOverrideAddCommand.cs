using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Chats;

/// <summary>
/// Admin override — tạo bình luận dù ticket đang <c>Closed</c>/<c>ClosedPendingRate</c> (#517).
/// Endpoint riêng biệt, bắt buộc <see cref="OverrideReason"/>, chỉ Admin gọi được.
/// </summary>
public class ChatOverrideAddCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    [JsonIgnore]
    public Guid TicketId { get; set; }
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

    /// <summary>
    /// Nội dung chi tiết.
    /// </summary>
    public required string Body { get; set; }
    public bool IsInternal { get; set; }
    public ChatBodyFormatEnum BodyFormat { get; set; } = ChatBodyFormatEnum.PlainText;
    /// <summary>
    /// Danh sách các tệp đính kèm.
    /// </summary>
    public List<ChatAttachmentInput>? Attachments { get; set; }
    public required string OverrideReason { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (UserId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "UserId không hợp lệ." });

        if (string.IsNullOrWhiteSpace(Body))
            response.ListErrors.Add(new Errors { Field = "Body", Detail = "Nội dung bình luận không được để trống." });
        else if (Body.Length > 10000)
            response.ListErrors.Add(new Errors { Field = "Body", Detail = "Nội dung bình luận tối đa 10000 ký tự." });

        if (string.IsNullOrWhiteSpace(OverrideReason))
            response.ListErrors.Add(new Errors { Field = "OverrideReason", Detail = "Bắt buộc nhập lý do override khi ticket đã đóng." });
        else if (OverrideReason.Length > 1000)
            response.ListErrors.Add(new Errors { Field = "OverrideReason", Detail = "Lý do override tối đa 1000 ký tự." });

        if (Attachments != null && Attachments.Any())
        {
            for (int i = 0; i < Attachments.Count; i++)
            {
                var att = Attachments[i];
                if (att.FileId == Guid.Empty)
                    response.ListErrors.Add(new Errors { Field = $"Attachments[{i}].FileId", Detail = "FileId không được để trống." });
                if (string.IsNullOrWhiteSpace(att.FileName))
                    response.ListErrors.Add(new Errors { Field = $"Attachments[{i}].FileName", Detail = "FileName không được để trống." });
                if (string.IsNullOrWhiteSpace(att.ContentType))
                    response.ListErrors.Add(new Errors { Field = $"Attachments[{i}].ContentType", Detail = "ContentType không được để trống." });
            }
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
