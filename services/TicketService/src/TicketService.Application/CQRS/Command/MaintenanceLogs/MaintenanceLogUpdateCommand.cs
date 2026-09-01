using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.MaintenanceLogs;

public class MaintenanceLogUpdateCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    /// <summary>
    /// ID của nhật ký bảo trì.
    /// </summary>
    [JsonIgnore]
    public Guid LogId { get; set; }
    [JsonIgnore]
    public Guid StaffId { get; set; }

    /// <summary>
    /// Log type.
    /// </summary>
    public MaintenanceLogTypeEnum? LogType { get; set; }
    public string? Summary { get; set; }
    public string? DiagnosisDetails { get; set; }
    /// <summary>
    /// Actions taken.
    /// </summary>
    public string? ActionsTaken { get; set; }
    public int? DurationMinutes { get; set; }
    public string? ResolutionNote { get; set; }
    /// <summary>
    /// Parts used.
    /// </summary>
    public string? PartsUsed { get; set; }
    public List<MaintenanceAttachmentInput>? Attachments { get; set; }
    public List<MaintenanceAttachmentInput>? BeforePhotos { get; set; }
    /// <summary>
    /// After photos.
    /// </summary>
    public List<MaintenanceAttachmentInput>? AfterPhotos { get; set; }
    public List<Guid>? RelatedKbArticleIds { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (LogId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "LogId", Detail = "Invalid LogId." });

        if (StaffId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "StaffId", Detail = "Invalid StaffId." });

        if (Summary != null && string.IsNullOrWhiteSpace(Summary))
            response.ListErrors.Add(new Errors { Field = "Summary", Detail = "Summary must not be empty when updated." });
        else if (Summary != null && Summary.Trim().Length > 500)
            response.ListErrors.Add(new Errors { Field = "Summary", Detail = "Summary must be at most 500 characters." });

        if (DurationMinutes.HasValue && DurationMinutes < 0)
            response.ListErrors.Add(new Errors { Field = "DurationMinutes", Detail = "Invalid duration." });


        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
