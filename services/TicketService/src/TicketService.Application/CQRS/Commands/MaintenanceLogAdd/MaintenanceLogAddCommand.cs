using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Ticket;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Commands.MaintenanceLogAdd;

public record MaintenanceLogAddCommand(
    Guid TicketId,
    Guid StaffId,
    MaintenanceLogTypeEnum LogType,
    string Summary,
    string? DiagnosisDetails,
    string? ActionsTaken,
    int DurationMinutes,
    string? ResolutionNote,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string? PartsUsed,
    List<MaintenanceAttachmentInput>? Attachments = null,
    List<MaintenanceAttachmentInput>? BeforePhotos = null,
    List<MaintenanceAttachmentInput>? AfterPhotos = null,
    List<Guid>? RelatedKbArticleIds = null,
    decimal? CheckInLatitude = null,
    decimal? CheckInLongitude = null,
    DateTime? CheckInAt = null
) : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
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
