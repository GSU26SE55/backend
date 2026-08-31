using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.MaintenanceLogs;

public class MaintenanceLogAddCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; set; }
    [JsonIgnore]
    public Guid StaffId { get; set; }

    /// <summary>
    /// Log type.
    /// </summary>
    public MaintenanceLogTypeEnum LogType { get; set; }
    public required string Summary { get; set; }
    public string? DiagnosisDetails { get; set; }
    /// <summary>
    /// Actions taken.
    /// </summary>
    public string? ActionsTaken { get; set; }
    public int DurationMinutes { get; set; }
    public string? ResolutionNote { get; set; }
    /// <summary>
    /// Started at.
    /// </summary>
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? PartsUsed { get; set; }
    /// <summary>
    /// Danh sách các tệp đính kèm.
    /// </summary>
    public List<MaintenanceAttachmentInput>? Attachments { get; set; }
    public List<MaintenanceAttachmentInput>? BeforePhotos { get; set; }
    public List<MaintenanceAttachmentInput>? AfterPhotos { get; set; }
    /// <summary>
    /// Related kb article ids.
    /// </summary>
    public List<Guid>? RelatedKbArticleIds { get; set; }
    public decimal? CheckInLatitude { get; set; }
    public decimal? CheckInLongitude { get; set; }
    /// <summary>
    /// Check in at.
    /// </summary>
    public DateTime? CheckInAt { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "Invalid TicketId." });

        if (StaffId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "StaffId", Detail = "Invalid StaffId." });

        if (string.IsNullOrWhiteSpace(Summary))
            response.ListErrors.Add(new Errors { Field = "Summary", Detail = "Summary is required." });
        // Cột summary chỉ có 500 ký tự. Không chặn ở đây thì validate cho qua rồi EF/Npgsql
        // ném ở tầng DB → 500 Internal Server Error chứ không phải 400 kèm listErrors, user
        // thấy "lỗi máy chủ" và mất trắng nội dung vừa gõ.
        else if (Summary.Trim().Length > 500)
            response.ListErrors.Add(new Errors { Field = "Summary", Detail = "Summary must be at most 500 characters." });

        if (DurationMinutes < 0)
            response.ListErrors.Add(new Errors { Field = "DurationMinutes", Detail = "Invalid duration." });

        if (StartedAt == default)
            response.ListErrors.Add(new Errors { Field = "StartedAt", Detail = "Invalid start time." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}

public record MaintenanceAttachmentInput(
    Guid FileId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Url = null
);
