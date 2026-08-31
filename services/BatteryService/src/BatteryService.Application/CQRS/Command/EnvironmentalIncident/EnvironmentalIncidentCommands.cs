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
    /// <summary>ID Site (Guid).</summary>
    public Guid SiteId { get; set; }
    /// <summary>Loại incident (SmokeDetected | WaterLeak | ...).</summary>
    public EnvironmentalIncidentTypeEnum IncidentType { get; set; }
    /// <summary>Severity của alert (Warning | Critical).</summary>
    public AlertSeverityEnum Severity { get; set; } = AlertSeverityEnum.Critical;
    /// <summary>User ID đã report incident.</summary>
    public string? ReportedBy { get; set; }
    /// <summary>Timestamp phát hiện (UTC).</summary>
    public DateTime DetectedAt { get; set; }
    /// <summary>Ghi chú tự do.</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// GH-806 — site của thiết bị đã xác thực, lấy từ claim <c>iot:site_id</c>.
    /// <c>null</c> khi người gọi là con người dùng JWT (endpoint report thủ công).
    /// </summary>
    /// <remarks>
    /// <c>[JsonIgnore][BindNever]</c>: client KHÔNG được đặt trường này qua body — nếu không, thiết
    /// bị chỉ cần tự khai site của mình là đi vòng qua toàn bộ hàng rào.
    /// </remarks>
    [JsonIgnore]
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public Guid? AuthenticatedDeviceSiteId { get; set; }

    public Task<EnvironmentalIncidentResponse> ValidateAsync()
    {
        var response = new EnvironmentalIncidentResponse();

        if (SiteId == Guid.Empty)
            AddError(response, nameof(SiteId), "SiteId is required.");

        if (!Enum.IsDefined(typeof(EnvironmentalIncidentTypeEnum), IncidentType))
            AddError(response, nameof(IncidentType), "Invalid IncidentType.");

        if (!Enum.IsDefined(typeof(AlertSeverityEnum), Severity))
            AddError(response, nameof(Severity), "Invalid Severity.");

        if (DetectedAt == default)
            AddError(response, nameof(DetectedAt), "DetectedAt is required.");
        else if (DetectedAt > DateTime.UtcNow.AddMinutes(5))
            AddError(response, nameof(DetectedAt), "DetectedAt cannot be more than 5 minutes ahead of the current time.");

        // Đo trên chuỗi đã trim: FE trim trước khi validate, để raw thì khoảng trắng cuối
        // (người dùng không nhìn thấy) đẩy độ dài qua ngưỡng và sinh 400 khó hiểu.
        if (ReportedBy?.Trim().Length > 256)
            AddError(response, nameof(ReportedBy), "ReportedBy must not exceed 256 characters.");

        if (Notes?.Trim().Length > 1000)
            AddError(response, nameof(Notes), "Notes must not exceed 1000 characters.");

        return Task.FromResult(response);
    }

    private static void AddError(EnvironmentalIncidentResponse response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Invalid incident report data.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}

public class AcknowledgeEnvironmentalIncidentCommand
    : IRequest<EnvironmentalIncidentResponse>, IValidatable<EnvironmentalIncidentResponse>
{
    /// <summary>Định danh resource.</summary>
    [JsonIgnore]
    public Guid Id { get; set; }
    /// <summary>User ID acknowledge incident.</summary>
    public Guid AcknowledgedBy { get; set; }

    public Task<EnvironmentalIncidentResponse> ValidateAsync()
    {
        var response = new EnvironmentalIncidentResponse();

        if (Id == Guid.Empty)
            AddError(response, nameof(Id), "Incident Id is required.");

        if (AcknowledgedBy == Guid.Empty)
            AddError(response, nameof(AcknowledgedBy), "Unable to determine the acknowledging user (invalid token).");

        return Task.FromResult(response);
    }

    private static void AddError(EnvironmentalIncidentResponse response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Invalid incident acknowledge data.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}

public class ResolveEnvironmentalIncidentCommand
    : IRequest<EnvironmentalIncidentResponse>, IValidatable<EnvironmentalIncidentResponse>
{
    /// <summary>Định danh resource.</summary>
    [JsonIgnore]
    public Guid Id { get; set; }
    /// <summary>User ID resolve incident.</summary>
    public Guid ResolvedBy { get; set; }
    /// <summary>Ghi chú khi resolve.</summary>
    public string ResolutionNote { get; set; } = string.Empty;

    public Task<EnvironmentalIncidentResponse> ValidateAsync()
    {
        var response = new EnvironmentalIncidentResponse();

        if (Id == Guid.Empty)
            AddError(response, nameof(Id), "Incident Id is required.");

        if (ResolvedBy == Guid.Empty)
            AddError(response, nameof(ResolvedBy), "Unable to determine the resolving user (invalid token).");

        if (string.IsNullOrWhiteSpace(ResolutionNote))
            AddError(response, nameof(ResolutionNote), "ResolutionNote is required.");
        else if (ResolutionNote.Trim().Length is < 5 or > 2000)
            AddError(response, nameof(ResolutionNote), "ResolutionNote must be 5-2000 characters long.");

        return Task.FromResult(response);
    }

    private static void AddError(EnvironmentalIncidentResponse response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Invalid incident resolve data.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}

public class MarkFalseAlarmEnvironmentalIncidentCommand
    : IRequest<EnvironmentalIncidentResponse>, IValidatable<EnvironmentalIncidentResponse>
{
    /// <summary>Định danh resource.</summary>
    [JsonIgnore]
    public Guid Id { get; set; }
    /// <summary>Field FalseAlarmBy.</summary>
    public Guid FalseAlarmBy { get; set; }
    /// <summary>Field FalseAlarmReason.</summary>
    public string FalseAlarmReason { get; set; } = string.Empty;

    public Task<EnvironmentalIncidentResponse> ValidateAsync()
    {
        var response = new EnvironmentalIncidentResponse();

        if (Id == Guid.Empty)
            AddError(response, nameof(Id), "Incident Id is required.");

        if (FalseAlarmBy == Guid.Empty)
            AddError(response, nameof(FalseAlarmBy), "Unable to determine the user marking the false alarm (invalid token).");

        if (string.IsNullOrWhiteSpace(FalseAlarmReason))
            AddError(response, nameof(FalseAlarmReason), "FalseAlarmReason is required.");
        else if (FalseAlarmReason.Trim().Length is < 5 or > 2000)
            AddError(response, nameof(FalseAlarmReason), "FalseAlarmReason must be 5-2000 characters long.");

        return Task.FromResult(response);
    }

    private static void AddError(EnvironmentalIncidentResponse response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Invalid false-alarm data.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}
