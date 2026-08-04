using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Tickets;

public class TicketCreateCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    /// <summary>
    /// Tiêu đề.
    /// </summary>
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TicketCategoryEnum Category { get; set; }

    /// <summary>
    /// Danh sách ID thiết bị pin (bắt buộc có ít nhất một pin).
    /// </summary>
    public List<Guid> BatteryAssetIds { get; set; } = new();

    /// <summary>
    /// Thời điểm phát hiện sự cố (bắt buộc).
    /// </summary>
    public DateTime? IncidentDetectedAt { get; set; }

    public List<TicketAttachmentInput> Attachments { get; set; } = new();

    [JsonIgnore]
    public Guid CustomerId { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (string.IsNullOrWhiteSpace(Title))
            response.ListErrors.Add(new Errors { Field = "Title", Detail = "Tiêu đề không được để trống." });

        if (string.IsNullOrWhiteSpace(Description))
            response.ListErrors.Add(new Errors { Field = "Description", Detail = "Mô tả không được để trống." });

        if (CustomerId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "CustomerId", Detail = "CustomerId không hợp lệ." });

        if (BatteryAssetIds.Count == 0)
            response.ListErrors.Add(new Errors { Field = "BatteryAssetIds", Detail = "Phải chọn ít nhất một pin." });
        else if (BatteryAssetIds.Any(id => id == Guid.Empty))
            response.ListErrors.Add(new Errors { Field = "BatteryAssetIds", Detail = "Danh sách pin không được chứa ID rỗng." });
        else if (BatteryAssetIds.Distinct().Count() != BatteryAssetIds.Count)
            response.ListErrors.Add(new Errors { Field = "BatteryAssetIds", Detail = "Danh sách pin không được trùng lặp." });

        if (!IncidentDetectedAt.HasValue)
            response.ListErrors.Add(new Errors { Field = "IncidentDetectedAt", Detail = "Thời điểm phát hiện sự cố không được để trống." });

        if (IncidentDetectedAt.HasValue && IncidentDetectedAt.Value > DateTime.UtcNow)
            response.ListErrors.Add(new Errors { Field = "IncidentDetectedAt", Detail = "Thời điểm phát hiện sự cố không được trong tương lai." });

        foreach (var attachment in Attachments)
        {
            if (attachment.FileId == Guid.Empty)
                response.ListErrors.Add(new Errors { Field = "Attachments.FileId", Detail = "FileId không hợp lệ." });
            if (string.IsNullOrWhiteSpace(attachment.FileName) || attachment.FileName.Length > 256)
                response.ListErrors.Add(new Errors { Field = "Attachments.FileName", Detail = "FileName là bắt buộc và tối đa 256 ký tự." });
            if (string.IsNullOrWhiteSpace(attachment.ContentType) || attachment.ContentType.Length > 100)
                response.ListErrors.Add(new Errors { Field = "Attachments.ContentType", Detail = "ContentType là bắt buộc và tối đa 100 ký tự." });
            if (attachment.SizeBytes < 0)
                response.ListErrors.Add(new Errors { Field = "Attachments.SizeBytes", Detail = "SizeBytes không được âm." });
            if (string.IsNullOrWhiteSpace(attachment.Url) || attachment.Url.Length > 2000)
                response.ListErrors.Add(new Errors { Field = "Attachments.Url", Detail = "Url là bắt buộc và tối đa 2000 ký tự." });
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

public record TicketAttachmentInput(
    Guid FileId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Url);
