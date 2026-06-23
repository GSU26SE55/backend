using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.ChatAttachmentAdd;

public class ChatAttachmentAddCommand : IRequest<CommonResponse<TicketAttachmentDTO>>, IValidatable<CommonResponse<TicketAttachmentDTO>>
{
    [JsonIgnore]
    public Guid TicketId { get; set; }
    [JsonIgnore]
    public Guid ChatId { get; set; }
    [JsonIgnore]
    public Guid UserId { get; set; }
    [JsonIgnore]
    public ActorRoleEnum UserRole { get; set; }

    public required Guid FileId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }

    public Task<CommonResponse<TicketAttachmentDTO>> ValidateAsync()
    {
        var response = new CommonResponse<TicketAttachmentDTO>();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (ChatId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "ChatId", Detail = "ChatId không hợp lệ." });

        if (FileId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "FileId", Detail = "FileId không được để trống." });

        if (string.IsNullOrWhiteSpace(FileName))
            response.ListErrors.Add(new Errors { Field = "FileName", Detail = "FileName không được để trống." });

        if (string.IsNullOrWhiteSpace(ContentType))
            response.ListErrors.Add(new Errors { Field = "ContentType", Detail = "ContentType không được để trống." });

        if (SizeBytes <= 0)
            response.ListErrors.Add(new Errors { Field = "SizeBytes", Detail = "SizeBytes phải lớn hơn 0." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
