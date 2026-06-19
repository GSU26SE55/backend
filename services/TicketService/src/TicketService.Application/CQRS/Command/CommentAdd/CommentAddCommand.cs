using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.CommentAdd;

public class CommentAddCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    [JsonIgnore]
    public Guid TicketId { get; set; }
    [JsonIgnore]
    public Guid UserId { get; set; }
    [JsonIgnore]
    public ActorRoleEnum UserRole { get; set; }
    [JsonIgnore]
    public string UserDisplayName { get; set; } = string.Empty;

    public required string Body { get; set; }
    public bool IsInternal { get; set; }
    public List<CommentAttachmentInput>? Attachments { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (UserId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "UserId không hợp lệ." });

        if (string.IsNullOrWhiteSpace(Body))
            response.ListErrors.Add(new Errors { Field = "Body", Detail = "Nội dung bình luận không được để trống." });

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

public record CommentAttachmentInput(
    Guid FileId,
    string FileName,
    string ContentType,
    long SizeBytes
);
