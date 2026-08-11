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
            response.ListErrors.Add(new Errors { Field = "Title", Detail = "Title must not be empty." });

        if (string.IsNullOrWhiteSpace(Description))
            response.ListErrors.Add(new Errors { Field = "Description", Detail = "Description must not be empty." });

        if (CustomerId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "CustomerId", Detail = "Invalid CustomerId." });

        if (BatteryAssetIds.Count != 1)
            response.ListErrors.Add(new Errors { Field = "BatteryAssetIds", Detail = "Exactly one battery must be selected." });
        else if (BatteryAssetIds.Any(id => id == Guid.Empty))
            response.ListErrors.Add(new Errors { Field = "BatteryAssetIds", Detail = "Battery list must not contain empty IDs." });
        else if (BatteryAssetIds.Distinct().Count() != BatteryAssetIds.Count)
            response.ListErrors.Add(new Errors { Field = "BatteryAssetIds", Detail = "Battery list must not contain duplicates." });

        if (!IncidentDetectedAt.HasValue)
            response.ListErrors.Add(new Errors { Field = "IncidentDetectedAt", Detail = "Incident detection time must not be empty." });

        if (IncidentDetectedAt.HasValue && IncidentDetectedAt.Value > DateTime.UtcNow)
            response.ListErrors.Add(new Errors { Field = "IncidentDetectedAt", Detail = "Incident detection time must not be in the future." });

        foreach (var attachment in Attachments)
        {
            if (attachment.FileId == Guid.Empty)
                response.ListErrors.Add(new Errors { Field = "Attachments.FileId", Detail = "Invalid FileId." });
            if (string.IsNullOrWhiteSpace(attachment.FileName) || attachment.FileName.Length > 256)
                response.ListErrors.Add(new Errors { Field = "Attachments.FileName", Detail = "FileName is required and must be at most 256 characters." });
            if (string.IsNullOrWhiteSpace(attachment.ContentType) || attachment.ContentType.Length > 100)
                response.ListErrors.Add(new Errors { Field = "Attachments.ContentType", Detail = "ContentType is required and must be at most 100 characters." });
            if (attachment.SizeBytes < 0)
                response.ListErrors.Add(new Errors { Field = "Attachments.SizeBytes", Detail = "SizeBytes must not be negative." });
            if (string.IsNullOrWhiteSpace(attachment.Url) || attachment.Url.Length > 2000)
                response.ListErrors.Add(new Errors { Field = "Attachments.Url", Detail = "Url is required and must be at most 2000 characters." });
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
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
