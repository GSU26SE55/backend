using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.ChatAttachmentBatchAdd;

public class ChatAttachmentBatchAddCommand : IRequest<CommonResponse<List<TicketAttachmentDTO>>>, IValidatable<CommonResponse<List<TicketAttachmentDTO>>>
{
    [JsonIgnore] public Guid TicketId { get; set; }
    [JsonIgnore] public Guid ChatId { get; set; }
    [JsonIgnore] public Guid UserId { get; set; }
    [JsonIgnore] public ActorRoleEnum UserRole { get; set; }

    /// <summary>
    /// Files.
    /// </summary>
    public required List<AttachmentItem> Files { get; set; }

    public Task<CommonResponse<List<TicketAttachmentDTO>>> ValidateAsync()
    {
        var response = new CommonResponse<List<TicketAttachmentDTO>>();

        if (Files == null || Files.Count == 0)
            response.ListErrors.Add(new Errors { Field = "Files", Detail = "Danh sách file không được rỗng." });
        else
        {
            for (int i = 0; i < Files.Count; i++)
            {
                var f = Files[i];
                if (f.FileId == Guid.Empty)
                    response.ListErrors.Add(new Errors { Field = $"Files[{i}].FileId", Detail = "FileId không được để trống." });
                if (string.IsNullOrWhiteSpace(f.FileName))
                    response.ListErrors.Add(new Errors { Field = $"Files[{i}].FileName", Detail = "FileName không được để trống." });
                if (string.IsNullOrWhiteSpace(f.ContentType))
                    response.ListErrors.Add(new Errors { Field = $"Files[{i}].ContentType", Detail = "ContentType không được để trống." });
                if (f.SizeBytes <= 0)
                    response.ListErrors.Add(new Errors { Field = $"Files[{i}].SizeBytes", Detail = "SizeBytes phải lớn hơn 0." });
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

public class AttachmentItem
{
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
}
