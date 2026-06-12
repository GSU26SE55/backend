using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.EnvironmentalIncident;

/// <summary>
/// Sprint 5B #102 — Report mới environmental incident. Tạo incident + auto-create Alert
/// site-level + publish event.
/// </summary>
public class ReportEnvironmentalIncidentCommand
    : IRequest<EnvironmentalIncidentResponse>, IValidatable<EnvironmentalIncidentResponse>
{
    public Guid SiteId { get; set; }
    public EnvironmentalIncidentTypeEnum IncidentType { get; set; }
    public AlertSeverityEnum Severity { get; set; } = AlertSeverityEnum.Critical;
    public string? ReportedBy { get; set; }
    public DateTime DetectedAt { get; set; }
    public string? Notes { get; set; }

    public Task<EnvironmentalIncidentResponse> ValidateAsync()
    {
        var response = new EnvironmentalIncidentResponse();

        if (SiteId == Guid.Empty)
            AddError(response, nameof(SiteId), "SiteId là bắt buộc.");

        if (!Enum.IsDefined(typeof(EnvironmentalIncidentTypeEnum), IncidentType))
            AddError(response, nameof(IncidentType), "IncidentType không hợp lệ.");

        if (!Enum.IsDefined(typeof(AlertSeverityEnum), Severity))
            AddError(response, nameof(Severity), "Severity không hợp lệ.");

        if (DetectedAt == default)
            AddError(response, nameof(DetectedAt), "DetectedAt là bắt buộc.");
        else if (DetectedAt > DateTime.UtcNow.AddMinutes(5))
            AddError(response, nameof(DetectedAt), "DetectedAt không được vượt thời điểm hiện tại quá 5 phút.");

        if (ReportedBy?.Length > 256)
            AddError(response, nameof(ReportedBy), "ReportedBy tối đa 256 ký tự.");

        if (Notes?.Length > 1000)
            AddError(response, nameof(Notes), "Notes tối đa 1000 ký tự.");

        return Task.FromResult(response);
    }

    private static void AddError(EnvironmentalIncidentResponse response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Dữ liệu báo cáo incident không hợp lệ.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}

public class AcknowledgeEnvironmentalIncidentCommand
    : IRequest<EnvironmentalIncidentResponse>, IValidatable<EnvironmentalIncidentResponse>
{
    [JsonIgnore]
    public Guid Id { get; set; }
    public Guid AcknowledgedBy { get; set; }

    public Task<EnvironmentalIncidentResponse> ValidateAsync()
    {
        var response = new EnvironmentalIncidentResponse();

        if (Id == Guid.Empty)
            AddError(response, nameof(Id), "Id incident là bắt buộc.");

        if (AcknowledgedBy == Guid.Empty)
            AddError(response, nameof(AcknowledgedBy), "Không xác định được người acknowledge (token không hợp lệ).");

        return Task.FromResult(response);
    }

    private static void AddError(EnvironmentalIncidentResponse response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Dữ liệu acknowledge incident không hợp lệ.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}

public class ResolveEnvironmentalIncidentCommand
    : IRequest<EnvironmentalIncidentResponse>, IValidatable<EnvironmentalIncidentResponse>
{
    [JsonIgnore]
    public Guid Id { get; set; }
    public Guid ResolvedBy { get; set; }
    public string ResolutionNote { get; set; } = string.Empty;

    public Task<EnvironmentalIncidentResponse> ValidateAsync()
    {
        var response = new EnvironmentalIncidentResponse();

        if (Id == Guid.Empty)
            AddError(response, nameof(Id), "Id incident là bắt buộc.");

        if (ResolvedBy == Guid.Empty)
            AddError(response, nameof(ResolvedBy), "Không xác định được người resolve (token không hợp lệ).");

        if (string.IsNullOrWhiteSpace(ResolutionNote))
            AddError(response, nameof(ResolutionNote), "ResolutionNote là bắt buộc.");
        else if (ResolutionNote.Length is < 5 or > 2000)
            AddError(response, nameof(ResolutionNote), "ResolutionNote phải dài 5-2000 ký tự.");

        return Task.FromResult(response);
    }

    private static void AddError(EnvironmentalIncidentResponse response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Dữ liệu resolve incident không hợp lệ.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}

public class MarkFalseAlarmEnvironmentalIncidentCommand
    : IRequest<EnvironmentalIncidentResponse>, IValidatable<EnvironmentalIncidentResponse>
{
    [JsonIgnore]
    public Guid Id { get; set; }
    public Guid FalseAlarmBy { get; set; }
    public string FalseAlarmReason { get; set; } = string.Empty;

    public Task<EnvironmentalIncidentResponse> ValidateAsync()
    {
        var response = new EnvironmentalIncidentResponse();

        if (Id == Guid.Empty)
            AddError(response, nameof(Id), "Id incident là bắt buộc.");

        if (FalseAlarmBy == Guid.Empty)
            AddError(response, nameof(FalseAlarmBy), "Không xác định được người đánh dấu false alarm (token không hợp lệ).");

        if (string.IsNullOrWhiteSpace(FalseAlarmReason))
            AddError(response, nameof(FalseAlarmReason), "FalseAlarmReason là bắt buộc.");
        else if (FalseAlarmReason.Length is < 5 or > 2000)
            AddError(response, nameof(FalseAlarmReason), "FalseAlarmReason phải dài 5-2000 ký tự.");

        return Task.FromResult(response);
    }

    private static void AddError(EnvironmentalIncidentResponse response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Dữ liệu false-alarm không hợp lệ.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}
