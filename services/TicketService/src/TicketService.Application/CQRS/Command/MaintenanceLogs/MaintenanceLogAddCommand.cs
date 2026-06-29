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
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (StaffId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "StaffId", Detail = "StaffId không hợp lệ." });

        if (string.IsNullOrWhiteSpace(Summary))
            response.ListErrors.Add(new Errors { Field = "Summary", Detail = "Tóm tắt không được để trống." });

        if (DurationMinutes < 0)
            response.ListErrors.Add(new Errors { Field = "DurationMinutes", Detail = "Thời gian thực hiện không hợp lệ." });

        if (StartedAt == default)
            response.ListErrors.Add(new Errors { Field = "StartedAt", Detail = "Thời gian bắt đầu không hợp lệ." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}

public record MaintenanceAttachmentInput(
    Guid FileId,
    string FileName,
    string ContentType,
    long SizeBytes
);
