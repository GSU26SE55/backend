using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Chats;

public class ChatAttachmentAddCommand : IRequest<CommonResponse<TicketAttachmentDTO>>, IValidatable<CommonResponse<TicketAttachmentDTO>>
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
    /// ID tham chiếu của file trong hệ thống lưu trữ.
    /// </summary>
    public required Guid FileId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    /// <summary>
    /// Kích thước file tính bằng byte.
    /// </summary>
    public long SizeBytes { get; set; }
    public string? Url { get; set; }

    public Task<CommonResponse<TicketAttachmentDTO>> ValidateAsync()
    {
        var response = new CommonResponse<TicketAttachmentDTO>();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "Invalid TicketId." });

        if (ChatId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "ChatId", Detail = "Invalid ChatId." });

        if (FileId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "FileId", Detail = "FileId is required." });

        if (string.IsNullOrWhiteSpace(FileName))
            response.ListErrors.Add(new Errors { Field = "FileName", Detail = "FileName is required." });

        if (string.IsNullOrWhiteSpace(ContentType))
            response.ListErrors.Add(new Errors { Field = "ContentType", Detail = "ContentType is required." });

        if (SizeBytes <= 0)
            response.ListErrors.Add(new Errors { Field = "SizeBytes", Detail = "SizeBytes must be greater than 0." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
