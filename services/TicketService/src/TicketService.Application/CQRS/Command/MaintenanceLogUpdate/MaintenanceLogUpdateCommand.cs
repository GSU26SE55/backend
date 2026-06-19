using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.MaintenanceLogAdd;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.MaintenanceLogUpdate;

public class MaintenanceLogUpdateCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    [JsonIgnore]
    public Guid LogId { get; set; }
    [JsonIgnore]
    public Guid StaffId { get; set; }

    public MaintenanceLogTypeEnum? LogType { get; set; }
    public string? Summary { get; set; }
    public string? DiagnosisDetails { get; set; }
    public string? ActionsTaken { get; set; }
    public int? DurationMinutes { get; set; }
    public string? ResolutionNote { get; set; }
    public string? PartsUsed { get; set; }
    public List<MaintenanceAttachmentInput>? Attachments { get; set; }
    public List<MaintenanceAttachmentInput>? BeforePhotos { get; set; }
    public List<MaintenanceAttachmentInput>? AfterPhotos { get; set; }
    public List<Guid>? RelatedKbArticleIds { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (LogId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "LogId", Detail = "LogId không hợp lệ." });

        if (StaffId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "StaffId", Detail = "StaffId không hợp lệ." });

        if (Summary != null && string.IsNullOrWhiteSpace(Summary))
            response.ListErrors.Add(new Errors { Field = "Summary", Detail = "Tóm tắt không được để trống nếu được cập nhật." });

        if (DurationMinutes.HasValue && DurationMinutes < 0)
            response.ListErrors.Add(new Errors { Field = "DurationMinutes", Detail = "Thời gian thực hiện không hợp lệ." });


        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
